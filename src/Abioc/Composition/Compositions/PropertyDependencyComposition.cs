﻿// Copyright (c) 2017 James Skimming. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace Abioc.Composition.Compositions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// A composition to produce code for property dependency injection.
    /// </summary>
    public class PropertyDependencyComposition : IComposition
    {
        private static readonly string NewLine = Environment.NewLine;

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyDependencyComposition"/> class.
        /// </summary>
        /// <param name="inner">The <see cref="Inner"/> <see cref="IComposition"/>.</param>
        /// <param name="propertiesToInject">The list of <see cref="PropertiesToInject"/>.</param>
        public PropertyDependencyComposition(
            IComposition inner,
            (string property, Type type)[] propertiesToInject)
        {
            if (inner == null)
                throw new ArgumentNullException(nameof(inner));
            if (propertiesToInject == null)
                throw new ArgumentNullException(nameof(propertiesToInject));

            Inner = inner;
            PropertiesToInject = propertiesToInject;
        }

        /// <summary>
        /// Gets the <see cref="Inner"/> <see cref="IComposition"/>.
        /// </summary>
        public IComposition Inner { get; }

        /// <summary>
        /// Gets the type provided by the <see cref="Inner"/> <see cref="IComposition"/>.
        /// </summary>
        public Type Type => Inner.Type;

        /// <summary>
        /// Gets the list of properties to inject.
        /// </summary>
        public (string property, Type type)[] PropertiesToInject { get; }

        /// <inheritdoc />
        public string GetInstanceExpression(CompositionContainer container, bool simpleName)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            string methodName = GetComposeMethodName(container, simpleName);
            string parameter = RequiresConstructionContext(container) ? "context" : string.Empty;

            string expression = $"{methodName}({parameter})";
            return expression;
        }

        /// <inheritdoc />
        public string GetComposeMethodName(CompositionContainer container, bool simpleName)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            string methodName = "PropertyInjection" + Type.ToCompileMethodName(simpleName);
            return methodName;
        }

        /// <inheritdoc />
        public IEnumerable<string> GetMethods(CompositionContainer container, bool simpleName)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            IEnumerable<string> innerMethods = Inner.GetMethods(container, simpleName);
            foreach (string innerMethod in innerMethods)
            {
                yield return innerMethod;
            }

            string parameter = RequiresConstructionContext(container)
                ? $"{NewLine}    {container.ConstructionContext} context"
                : string.Empty;

            string methodName = GetComposeMethodName(container, simpleName);
            string signature = $"private {Type.ToCompileName()} {methodName}({parameter})";

            string instanceExpression = Inner.GetInstanceExpression(container, simpleName);
            instanceExpression = $"{NewLine}{Type.ToCompileName()} instance = {instanceExpression};";
            instanceExpression = CodeGen.Indent(instanceExpression);

            IEnumerable<string> propertyExpressions =
                GetPropertyExpressions(container)
                    .Select(
                        pe => $"instance.{pe.property} = {pe.expression.GetInstanceExpression(container, simpleName)};");

            string propertySetters = NewLine + string.Join(NewLine, propertyExpressions);
            propertySetters = CodeGen.Indent(propertySetters);

            string method = string.Format(
                "{0}{3}{{{1}{2}{3}    return instance;{3}}}",
                signature,
                instanceExpression,
                propertySetters,
                NewLine);
            yield return method;
        }

        /// <inheritdoc />
        public IEnumerable<string> GetFields(CompositionContainer container)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            return Inner.GetFields(container);
        }

        /// <inheritdoc />
        public IEnumerable<(string snippet, object value)> GetFieldInitializations(CompositionContainer container)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            return Inner.GetFieldInitializations(container);
        }

        /// <inheritdoc />
        public IEnumerable<string> GetAdditionalInitializations(CompositionContainer container, bool simpleName)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            return Inner.GetAdditionalInitializations(container, simpleName);
        }

        /// <inheritdoc />
        public bool RequiresConstructionContext(CompositionContainer container)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            return Inner.RequiresConstructionContext(container);
        }

        private IEnumerable<(string property, IParameterExpression expression)> GetPropertyExpressions(
            CompositionContainer container)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            foreach ((string property, Type type) in PropertiesToInject)
            {
                if (container.Compositions.TryGetValue(type, out IComposition composition))
                {
                    IParameterExpression expression = new SimpleParameterExpression(composition);
                    yield return (property, expression);
                    continue;
                }

                TypeInfo propertyTypeInfo = type.GetTypeInfo();
                if (propertyTypeInfo.IsGenericType)
                {
                    Type genericTypeDefinition = propertyTypeInfo.GetGenericTypeDefinition();
                    if (typeof(IEnumerable<>) == genericTypeDefinition)
                    {
                        Type enumerableType = propertyTypeInfo.GenericTypeArguments.Single();
                        IParameterExpression expression =
                            new EnumerableParameterExpression(enumerableType, container.ConstructionContext.Length > 0);
                        yield return (property, expression);
                        continue;
                    }
                }

                string message =
                    $"Failed to get the composition for the property '{type.Name} {property}' of the instance " +
                    $"'{Type}'. Is there a missing registration mapping?";
                throw new CompositionException(message);
            }
        }
    }
}
