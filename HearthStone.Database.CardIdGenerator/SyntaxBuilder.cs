#region

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HearthDb;
using HearthDb.Enums;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#endregion

namespace HearthStone.Database.CardIdGenerator
{
	internal class SyntaxBuilder
	{
		private static Dictionary<string, List<string>> _namingConflicts = new Dictionary<string, List<string>>();

		internal static ClassDeclarationSyntax GetNonCollectible()
		{
			while(true)
			{
				var newNamingConflicts = new Dictionary<string, List<string>>();
				var classDecl = SyntaxFactory.ClassDeclaration("NonCollectible").AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
				foreach(var c in ClassNames)
				{
					var className = c == "DREAM" ? "DreamCards" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(c.ToLower());
					var cCard = SyntaxFactory.ClassDeclaration(className).AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
					var anyCards = false;
					foreach(var card in
						Cards.All.OrderBy(x => x.Value.Set)
							 .ThenBy(x => x.Key)
							 .Select(x => x.Value)
							 .Where(x => !x.Collectible && x.Class.ToString().Equals(c)))
					{
						if(card.Name == null)
							continue;
						var name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(card.Name.ToLower());
						name = Regex.Replace(name, @"[^\w\d]", "");
						name = ResolveNameFromId(card, name);
						name = ResolveNamingConflict(name, card, newNamingConflicts, className);
						cCard = cCard.AddMembers(GenerateConst(name, card.Id));
						anyCards = true;
					}
					if(anyCards)
						classDecl = classDecl.AddMembers(cCard);
				}
				if(!newNamingConflicts.Any(x => x.Value.Count > 1))
					return classDecl;
				foreach(var pair in newNamingConflicts.Where(x => x.Value.Count > 1).ToDictionary(pair => pair.Key, pair => pair.Value))
					_namingConflicts.Add(pair.Key, pair.Value);
			}
		}

		private static string ResolveNameFromId(Card card, string name)
		{
			var baseId = Helper.GetBaseId(card.Id);
			if(baseId != card.Id)
			{
				Card baseCard;
				if(Cards.All.TryGetValue(baseId, out baseCard))
				{
					var tmpName = Regex.Replace(baseCard.Name, @"[^\w\d]", "");
					if(Regex.IsMatch(card.Id, @"_[\dabet]+[hH]"))
					{
						if(tmpName.StartsWith("Heroic"))
							tmpName = tmpName.Substring(6);
						tmpName += "Heroic";
					}
					name = tmpName + "_" + name;
				}
			}
			if(card.Set == HearthDb.Enums.CardSet.HERO_SKINS)
				name += "HeroSkins";
			if(Regex.IsMatch(card.Id, @"_\d+[abhHt]?[eo]"))
				name += "Enchantment";
			if(Regex.IsMatch(card.Id, @"_\d+[hH]?[t]"))
				name += "Token";
			if(Helper.SpecialPrefixes.ContainsKey(card.Id))
				name += Helper.SpecialPrefixes[card.Id];
			if(Regex.IsMatch(card.Id, @"_2_TB$"))
				name += "TavernBrawlHeroPower";
			else if(Regex.IsMatch(card.Id, @"_TB$") || card.Id.StartsWith("TB"))
				name += "TavernBrawl";
			else if(card.Id == "BRM_027h")
				name += "Hero";
			else if(card.Id == "BRM_027p")
				name += "HeroPower";
			else if(Regex.IsMatch(card.Id, @"_[\dabet]+[hH]") && !(name.Contains("_") && name.Split('_')[0].Contains("Heroic")))
			{
				if(name.StartsWith("Heroic"))
					name = name.Substring(6);
				name += "Heroic";
			}
			if(Regex.IsMatch(name, @"^\d"))
				name = "_" + name;
			return name;
		}

		private static string ResolveNamingConflict(string name, Card card, Dictionary<string, List<string>> newNamingConflicts, string className)
		{
			List<string> conflictingIds;
			if(_namingConflicts.TryGetValue(name + Helper.GetSetAbbreviation(card.Set), out conflictingIds) && conflictingIds.Contains(card.Id))
				name += Helper.GetSetAbbreviation(card.Set) + (conflictingIds.IndexOf(card.Id) + 1);
			else if(_namingConflicts.TryGetValue(name, out conflictingIds))
			{
				if(conflictingIds.Any(x => x.Substring(0, 3) != card.Id.Substring(0, 3)))
					name += Helper.GetSetAbbreviation(card.Set);
				else
					name += (conflictingIds.IndexOf(card.Id) + 1).ToString();
			}
			else if(className == name)
				name += Helper.GetSetAbbreviation(card.Set);
			List<string> ids;
			if(!newNamingConflicts.TryGetValue(name, out ids))
			{
				ids = new List<string>();
				newNamingConflicts.Add(name, ids);
			}
			ids.Add(card.Id);
			return name;
		}

		internal static ClassDeclarationSyntax GetCollectible(ClassDeclarationSyntax classDecl)
		{
			foreach(var c in ClassNames)
			{
				var anyCards = false;
				var className = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(c.ToLower());
				var cCard = SyntaxFactory.ClassDeclaration(className).AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
				foreach(var card in
					Cards.All.Values.Where(x => x.Collectible && x.Class.ToString().Equals(c)))
				{
					var name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(card.Name.ToLower());
					name = Regex.Replace(name, @"[^\w\d]", "");
					cCard = cCard.AddMembers(GenerateConst(name, card.Id));
					anyCards = true;
				}
				if(anyCards)
					classDecl = classDecl.AddMembers(cCard);
			}
			return classDecl;
		}

		private static IEnumerable<string> ClassNames
			=> new[] {CardClass.NEUTRAL.ToString()}.Concat(Enum.GetNames(typeof(CardClass))).Distinct();

		internal static FieldDeclarationSyntax GenerateConst(string identifier, string value)
		{
			var assignedValue = SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value)));
			var declaration = SyntaxFactory.SeparatedList(new[] {SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(identifier), null, assignedValue)});
			return
				SyntaxFactory.FieldDeclaration(SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName("string"), declaration))
					.AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
					.AddModifiers(SyntaxFactory.Token(SyntaxKind.ConstKeyword));
		}
	}
}
