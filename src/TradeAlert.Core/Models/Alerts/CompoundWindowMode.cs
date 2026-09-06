namespace TradeAlert.Core.Models.Alerts;

/// <summary>
/// Compound event window semantics.
/// SourceOneShot: evaluate once per source-bar close (baseline).
/// AnchorLatchWindow: anchor primary event, latch OK state legs, retry pending within chart-bar window.
/// SetupAtCondFire: trade geometry (B/D) frozen at cond-fire bar N; rule fire = permission to submit;
///   C still waited normally; state legs retry within window; B/D not re-resolved at confirm bar.
/// </summary>
public enum CompoundWindowMode
{
    SourceOneShot = 0,
    AnchorLatchWindow = 1,
    SetupAtCondFire = 2,
}
