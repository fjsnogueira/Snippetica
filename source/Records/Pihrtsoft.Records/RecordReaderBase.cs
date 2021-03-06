﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using Pihrtsoft.Records.Commands;
using Pihrtsoft.Records.Utilities;

namespace Pihrtsoft.Records
{
    internal abstract class RecordReaderBase
    {
        public RecordReaderBase(XElement element, EntityDefinition entity, DocumentReaderSettings settings)
        {
            Element = element;
            Entity = entity;
            Settings = settings;
        }

        public XElement Element { get; }
        public EntityDefinition Entity { get; }
        public DocumentReaderSettings Settings { get; }
        internal XElement Current { get; private set; }

        private CommandCollection Commands { get; set; }
        private Stack<Variable> Variables { get; set; }

        public abstract IEnumerable<Record> ReadRecords();

        protected abstract void AddRecord(Record record);

        protected abstract Record CreateRecord(string id);

        protected void Collect(IEnumerable<XElement> elements)
        {
            foreach (XElement element in elements)
            {
                Current = element;

                switch (element.Kind())
                {
                    case ElementKind.New:
                        {
                            AddRecord(CreateRecord(element));

                            break;
                        }
                    case ElementKind.Command:
                        {
                            if (element.HasElements)
                            {
                                AddPendingCommands(element);
                                Collect(element.Elements());
                                Commands.RemoveLast();
                            }

                            break;
                        }
                    case ElementKind.Variable:
                        {
                            if (element.HasElements)
                            {
                                AddVariable(element);
                                Collect(element.Elements());
                                Variables.Pop();
                            }

                            break;
                        }
                    default:
                        {
                            ThrowHelper.UnknownElement(element);
                            break;
                        }
                }

                Current = null;
            }
        }

        private void AddPendingCommands(XElement element)
        {
            using (IEnumerator<Command> en = CreateCommandFromElement(element).GetEnumerator())
            {
                if (en.MoveNext())
                {
                    Command command = en.Current;

                    if (en.MoveNext())
                    {
                        var commands = new List<Command>();
                        commands.Add(command);
                        commands.Add(en.Current);

                        while (en.MoveNext())
                            commands.Add(en.Current);

                        AddCommand(new GroupCommand(commands));
                    }
                    else
                    {
                        AddCommand(command);
                    }
                }
            }
        }

        private void AddCommand(Command command)
        {
            if (Commands == null)
                Commands = new CommandCollection();

            Commands.Add(command);
        }

        private void AddVariable(XElement element)
        {
            string name = element.AttributeValueOrThrow(AttributeNames.Name);
            string value = element.AttributeValueOrThrow(AttributeNames.Value);

            if (Variables == null)
                Variables = new Stack<Variable>();

            Variables.Push(new Variable(name, value));
        }

        private Record CreateRecord(XElement element)
        {
            string id = null;

            CommandCollection commands = null;

            foreach (XAttribute attribute in element.Attributes())
            {
                if (DefaultComparer.NameEquals(attribute, AttributeNames.Id))
                {
                    id = GetValue(attribute);
                }
                else
                {
                    if (commands == null)
                        commands = new CommandCollection();

                    commands.Add(CreateCommandFromAttribute(attribute));
                }
            }

            Record record = CreateRecord(id);

            if (commands != null)
                commands.ExecuteAll(record);

            GetChildCommands(element).ExecuteAll(record);

            Commands?.ExecuteAll(record);

            SetDefaultValues(record);

            return record;
        }

        private void SetDefaultValues(Record record)
        {
            foreach (PropertyDefinition property in Entity.AllProperties()
                .Where(f => f.DefaultValue != null))
            {
                if (!record.ContainsProperty(property.Name))
                {
                    if (property.IsCollection)
                    {
                        var list = new List<object>();
                        list.Add(property.DefaultValue);
                        record[property.Name] = list;
                    }
                    else
                    {
                        record[property.Name] = property.DefaultValue;
                    }
                }
            }
        }

        private Command CreateCommandFromAttribute(XAttribute attribute)
        {
            if (DefaultComparer.NameEquals(attribute, AttributeNames.Tag))
            {
                return new AddTagCommand(GetValue(attribute));
            }
            else
            {
                string propertyName = GetPropertyName(attribute);

                string value = GetValue(attribute);

                PropertyDefinition propertyDefinition = Entity.FindProperty(propertyName);

                if (propertyDefinition?.IsCollection == true)
                    return new AddItemCommand(propertyName, value);

                return new SetCommand(propertyName, value);
            }
        }

        private IEnumerable<Command> GetChildCommands(XElement parent)
        {
            foreach (XElement element in parent.Elements())
            {
                Current = element;

                if (element.HasAttributes)
                {
                    if (element.Kind() != ElementKind.Command)
                        ThrowHelper.UnknownElement(element);

                    foreach (Command command in CreateCommandFromElement(element))
                        yield return command;
                }
                else
                {
                    string propertyName = GetPropertyName(element);

                    string value = GetValue(element);

                    PropertyDefinition propertyDefinition = Entity.FindProperty(propertyName);

                    if (propertyDefinition?.IsCollection == true)
                    {
                        yield return new AddItemCommand(propertyName, value);
                    }
                    else
                    {
                        yield return new SetCommand(propertyName, value);
                    }
                }
            }

            Current = parent;
        }

        private IEnumerable<Command> CreateCommandFromElement(XElement element)
        {
            Debug.Assert(element.HasAttributes, element.ToString());

            switch (element.LocalName())
            {
                case ElementNames.Set:
                    {
                        foreach (XAttribute attribute in element.Attributes())
                            yield return CreateCommandFromAttribute(attribute);

                        break;
                    }
                case ElementNames.Append:
                    {
                        foreach (XAttribute attribute in element.Attributes())
                            yield return new AppendCommand(GetPropertyName(attribute), GetValue(attribute));

                        break;
                    }
                case ElementNames.Prefix:
                    {
                        foreach (XAttribute attribute in element.Attributes())
                            yield return new PrefixCommand(GetPropertyName(attribute), GetValue(attribute));

                        break;
                    }
                case ElementNames.Tag:
                    {
                        XAttribute attribute = element
                            .Attributes()
                            .FirstOrDefault(f => f.LocalName() == AttributeNames.Value);

                        if (attribute != null)
                            yield return new AddTagCommand(GetValue(attribute));

                        break;
                    }
                case ElementNames.Add:
                    {
                        foreach (XAttribute attribute in element.Attributes())
                        {
                            string propertyName = GetAttributeName(attribute);

                            PropertyDefinition property = Entity.FindProperty(propertyName);

                            if (property == null)
                            {
                                Throw(ExceptionMessages.PropertyIsNotDefined(propertyName));
                            }
                            else if (!property.IsCollection)
                            {
                                Throw(ExceptionMessages.CannotAddItemToNonCollectionProperty(propertyName));
                            }

                            yield return new AddItemCommand(propertyName, GetValue(attribute));
                        }

                        break;
                    }
                default:
                    {
                        Throw(ExceptionMessages.CommandIsNotDefined(element.LocalName()));
                        break;
                    }
            }
        }

        private string GetPropertyName(XAttribute attribute)
        {
            string propertyName = GetAttributeName(attribute);

            if (!Entity.ContainsProperty(propertyName))
                Throw(ExceptionMessages.PropertyIsNotDefined(propertyName), attribute);

            return propertyName;
        }

        private string GetPropertyName(XElement element)
        {
            string propertyName = GetAttributeName(element);

            if (!Entity.ContainsProperty(propertyName))
                Throw(ExceptionMessages.PropertyIsNotDefined(propertyName));

            return propertyName;
        }

        private string GetAttributeName(XAttribute attribute)
        {
            return attribute.LocalName();
        }

        private string GetAttributeName(XElement element)
        {
            return element.LocalName();
        }

        private string GetValue(XAttribute attribute)
        {
            return GetValue(attribute, attribute.Value);
        }

        private string GetValue(XElement element)
        {
            return GetValue(element, element.Value);
        }

        private string GetValue(XObject xobject, string value)
        {
            try
            {
                return AttributeValueParser.GetAttributeValue(value, this);
            }
            catch (InvalidValueException ex)
            {
                ThrowHelper.ThrowInvalidOperation("Error while parsing value.", xobject, ex);
            }

            return null;
        }

        internal Variable FindVariable(string name)
        {
            if (Variables != null)
            {
                Variable variable = Variables.FirstOrDefault(f => DefaultComparer.NameEquals(name, f.Name));

                if (variable != null)
                    return variable;
            }

            return Entity.FindVariable(name);
        }

        internal void Throw(string message, XObject @object = null)
        {
            ThrowHelper.ThrowInvalidOperation(message, @object ?? Current);
        }
    }
}
