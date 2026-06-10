using System.Collections.Generic;
using QF = global::QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// Custom MessageFactory som skapar generiska Message-objekt.
    /// DefaultMessageFactory används inte — den triggar StackOverflow på net4.8 via
    /// AppDomain-assembly-scan i konstruktorn.
    /// Med UseDataDictionary=Y hanterar data dictionary all group-struktur;
    /// typed subklasser krävs inte för tag-baserad meddelandeläsning.
    /// </summary>
    public class LenientMessageFactory : QF.IMessageFactory
    {
        public QF.Message Create(string beginString, string msgType)
        {
            return new QF.Message();
        }

        public QF.Message Create(string beginString, QF.Fields.ApplVerID applVerID, string msgType)
        {
            return new QF.Message();
        }

        public QF.Group Create(string beginString, string msgType, int groupCounterTag)
        {
            var delimiterTag = GetDelimiterTag(groupCounterTag);
            return new QF.Group(groupCounterTag, delimiterTag);
        }

        public ICollection<string> GetSupportedBeginStrings()
        {
            return new List<string> { "FIX.4.4" };
        }

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
                683 => 688,   // NoLegStipulations → LegStipulationType
                539 => 524,   // NoNestedPartyIDs → NestedPartyID
                804 => 545,   // NoNestedPartySubIDs → NestedPartySubID
                _ => 0
            };
        }
    }
}