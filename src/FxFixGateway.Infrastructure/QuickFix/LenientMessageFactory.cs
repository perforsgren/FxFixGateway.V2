using System;
using System.Collections.Generic;
using QF = global::QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// Custom MessageFactory som skapar Message-objekt utan strikt validering.
    /// Löser problemet med multi-leg FIX-meddelanden (duplicerade tags som 600)
    /// som QuickFIX/n rejectar även med UseDataDictionary=N.
    /// 
    /// Med UseDataDictionary=Y och en korrekt data dictionary behövs denna factory
    /// främst som fallback - dictionary definierar repeating groups (NoLegs etc.)
    /// så att QuickFIX kan parsa multi-leg meddelanden korrekt.
    /// </summary>
    public class LenientMessageFactory : QF.IMessageFactory
    {
        private readonly QF.IMessageFactory _defaultFactory = new QF.DefaultMessageFactory();

        public QF.Message Create(string beginString, string msgType)
        {
            // Låt default factory hantera ALLA meddelanden när data dictionary finns.
            // Den skapar rätt Message-subklass med group definitions.
            try
            {
                return _defaultFactory.Create(beginString, msgType);
            }
            catch
            {
                // Fallback: generisk Message om default factory inte känner till MsgType
                return new QF.Message();
            }
        }

        public QF.Message Create(string beginString, QF.Fields.ApplVerID applVerID, string msgType)
        {
            try
            {
                return _defaultFactory.Create(beginString, applVerID, msgType);
            }
            catch
            {
                return new QF.Message();
            }
        }

        public QF.Group Create(string beginString, string msgType, int groupCounterTag)
        {
            try
            {
                return _defaultFactory.Create(beginString, msgType, groupCounterTag);
            }
            catch
            {
                // Fallback: skapa generisk grupp med känd delimiter-tag
                var delimiterTag = GetDelimiterTag(groupCounterTag);
                return new QF.Group(groupCounterTag, delimiterTag);
            }
        }

        public ICollection<string> GetSupportedBeginStrings()
        {
            return _defaultFactory.GetSupportedBeginStrings();
        }

        /// <summary>
        /// Mappar vanliga group counter tags till deras delimiter (första) tag.
        /// Används som fallback om data dictionary saknas.
        /// </summary>
        private static int GetDelimiterTag(int groupCounterTag)
        {
            return groupCounterTag switch
            {
                555 => 600,   // NoLegs → LegSymbol
                711 => 311,   // NoUnderlyings → UnderlyingSymbol
                552 => 54,    // NoSides → Side
                453 => 448,   // NoPartyIDs → PartyID
                802 => 523,   // NoPartySubIDs → PartySubID
                232 => 233,   // NoStipulations → StipulationType
                683 => 688,   // NoLegStipulations → LegStipulationType  ← SAKNAS
                539 => 524,   // NoNestedPartyIDs → NestedPartyID
                804 => 545,   // NoNestedPartySubIDs → NestedPartySubID
                _ => 0
            };
        }
    }
}