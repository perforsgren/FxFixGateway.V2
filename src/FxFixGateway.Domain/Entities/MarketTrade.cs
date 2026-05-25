namespace FxFixGateway.Domain.Entities
{
    /// <summary>
    /// En trade print ur en MarketDataSnapshot (MdEntryType=2).
    /// Sparas i fxvol.market_trades.
    /// 277=G = Given (bid hit), 277=T = Taken (offer lifted).
    /// </summary>
    public class MarketTrade
    {
        public long      Id             { get; set; }
        public string    SecurityId     { get; set; } = string.Empty;
        public string    SessionKey     { get; set; } = string.Empty;
        public string?   CurrencyPair   { get; set; }
        public string?   Tenor          { get; set; }  // från market_instruments
        public string?   Cut            { get; set; }  // från market_instruments (NY/TK/LN...)
        public string?   Strategy       { get; set; }  // från market_instruments (STRADDLE etc)
        public string?   Delta          { get; set; }  // från market_instruments (ATM/25D etc)
        public decimal?  Price          { get; set; }  // tag 270
        public decimal?  Size           { get; set; }  // tag 271
        public DateOnly? TradeDate      { get; set; }  // tag 272
        public TimeOnly? TradeTime      { get; set; }  // tag 273
        public string?   TradeCondition { get; set; }  // tag 277 (G=Given/bid hit, T=Taken/offer lifted)
        public long      SnapshotId     { get; set; }
        public DateTime  ReceivedUtc    { get; set; }
    }
}