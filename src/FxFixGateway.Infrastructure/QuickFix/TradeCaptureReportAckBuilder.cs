using System;
using FxFixGateway.Domain.ValueObjects;
using QF = global::QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// Bygger TradeCaptureReportAck (MsgType=AR) meddelanden för att bekräfta
    /// mottagna TradeCaptureReport (AE) från venues.
    /// </summary>
    public class TradeCaptureReportAckBuilder
    {
        /// <summary>
        /// Bygger ett Accept AR-meddelande (TrdRptStatus=0).
        /// </summary>
        /// <param name="ack">PendingAck med information från tradesystemlink + messagein</param>
        /// <returns>QuickFIX AR message</returns>
        public QF.FIX44.TradeCaptureReportAck BuildAccept(PendingAck ack)
        {
            if (ack == null)
                throw new ArgumentNullException(nameof(ack));

            if (string.IsNullOrWhiteSpace(ack.TradeReportId))
                throw new ArgumentException("TradeReportId (571) is required", nameof(ack));

            // Required fields
            var ar = new QF.FIX44.TradeCaptureReportAck(
                new QF.Fields.TradeReportID(ack.TradeReportId),  // tag 571
                new QF.Fields.TrdRptStatus(0)                     // tag 939 (0=Accepted)
            );

            // Tag 17: ExecID (MX3 trade id från booking)
            if (!string.IsNullOrWhiteSpace(ack.InternTradeId))
            {
                ar.SetField(new QF.Fields.ExecID(ack.InternTradeId));
            }

            // TODO: Tag 881 (SecondaryTradeReportRefID) och 527 (SecondaryExecID)
            // behöver hämtas från messagein om Volbroker kräver dem.
            // För nu skippar vi dessa - kan lägga till om ACK failar.

            return ar;
        }

        /// <summary>
        /// Bygger ett Reject AR-meddelande (TrdRptStatus=1).
        /// </summary>
        /// <param name="ack">PendingAck med information</param>
        /// <param name="rejectReason">Rejection reason code (tag 751)</param>
        /// <returns>QuickFIX AR message</returns>
        public QF.FIX44.TradeCaptureReportAck BuildReject(PendingAck ack, int rejectReason = 0)
        {
            if (ack == null)
                throw new ArgumentNullException(nameof(ack));

            if (string.IsNullOrWhiteSpace(ack.TradeReportId))
                throw new ArgumentException("TradeReportId (571) is required", nameof(ack));

            var ar = new QF.FIX44.TradeCaptureReportAck(
                new QF.Fields.TradeReportID(ack.TradeReportId),
                new QF.Fields.TrdRptStatus(1) // 1=Rejected
            );

            // Tag 751: TradeReportRejectReason
            if (rejectReason > 0)
            {
                ar.SetField(new QF.Fields.TradeReportRejectReason(rejectReason));
            }

            return ar;
        }
    }
}
