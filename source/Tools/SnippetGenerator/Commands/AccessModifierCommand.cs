﻿namespace Pihrtsoft.Snippets.CodeGeneration.Commands
{
    public class AccessModifierCommand : BaseCommand
    {
        public AccessModifierCommand(ModifierDefinition modifier)
        {
            Modifier = modifier;
        }

        public override CommandKind Kind
        {
            get { return CommandKind.AccessModifier; }
        }

        public ModifierDefinition Modifier { get; }

        protected override void Execute(ExecutionContext context, Snippet snippet)
        {
            snippet.PrefixTitle($"{Modifier.Keyword} ");

            snippet.PrefixShortcut(Modifier.Shortcut);

            snippet.PrefixDescription($"{Modifier.Keyword} ");

            if (!Modifier.Tags.Contains(KnownTags.Default))
                snippet.AddTag(KnownTags.ExcludeFromReadme);

            snippet.RemoveLiteralAndReplacePlaceholders(LiteralIdentifiers.Modifiers, Modifier.Keyword);

            snippet.PrefixFileName(Modifier.Name);
        }
    }
}
