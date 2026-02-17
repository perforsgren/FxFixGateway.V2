using System;
using System.Collections.Generic;
using QF = global::QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// Custom MessageFactory som skapar Message-objekt utan strikt validering.
    /// Löser problemet med multi-leg FIX-meddelanden (duplicerade tags som 600)
    /// som QuickFIX/n rejectar även med UseDataDictionary=N.
    /// </summary>
    public class LenientMessageFactory : QF.IMessageFactory
    {
        private readonly QF.IMessageFactory _defaultFactory = new QF.DefaultMessageFactory();

        public QF.Message Create(string beginString, string msgType)
        {
            if (IsAdminMsgType(msgType))
            {
                return _defaultFactory.Create(beginString, msgType);
            }

            return new QF.Message();
        }

        public QF.Message Create(string beginString, QF.Fields.ApplVerID applVerID, string msgType)
        {
            if (IsAdminMsgType(msgType))
            {
                return _defaultFactory.Create(beginString, applVerID, msgType);
            }

            return new QF.Message();
        }

        public QF.Group Create(string beginString, string msgType, int groupCounterTag)
        {
            try
            {
                return _defaultFactory.Create(beginString, msgType, groupCounterTag);
            }
            catch
            {
                return new QF.Group(groupCounterTag, 0);
            }
        }

        public ICollection<string> GetSupportedBeginStrings()
        {
            return _defaultFactory.GetSupportedBeginStrings();
        }

        private static bool IsAdminMsgType(string msgType)
        {
            return msgType switch
            {
                "0" => true,  // Heartbeat
                "A" => true,  // Logon
                "1" => true,  // TestRequest
                "2" => true,  // ResendRequest
                "3" => true,  // Reject
                "4" => true,  // SequenceReset
                "5" => true,  // Logout
                _ => false
            };
        }
    }
}