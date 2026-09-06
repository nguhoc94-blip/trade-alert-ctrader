namespace TradeAlert.Core.Engines.AlertEvaluators;

public static class AlertReasonCodes
{
    public const string DepEventDetectorUnavailable = "DEP_EVENT_DETECTOR_UNAVAILABLE";
    public const string DepZoneCollectionUnavailable = "DEP_ZONE_COLLECTION_UNAVAILABLE";
    public const string DepRealtimeFilterUnavailable = "DEP_REALTIME_FILTER_UNAVAILABLE";
    public const string CarryOverMissingM15Edge = "CARRY_OVER_MISSING_M15_EDGE";
    public const string SkeletonGuardedLoop4 = "SKELETON_GUARDED_LOOP4";

    // Event family
    public const string EventFired         = "EVENT_FIRED";
    public const string EventNoBarClose    = "EVENT_NO_BAR_CLOSE";
    public const string EventNoTransition  = "EVENT_NO_TRANSITION";
    public const string EventEvaluated     = "EVENT_EVALUATED";

    // Touch family
    public const string TouchFired         = "TOUCH_FIRED";
    public const string TouchNotFired      = "TOUCH_NOT_FIRED";

    // Real family
    public const string RealFired          = "REAL_FIRED";
    public const string RealNotFired       = "REAL_NOT_FIRED";
    public const string RealAnchorAbsent   = "REAL_ANCHOR_ABSENT";

    // M15+Real composite
    public const string RealM15Fired       = "REAL_M15_FIRED";
    public const string RealM15NotFired    = "REAL_M15_NOT_FIRED";

    // Phá Khung Lớn (PKL) — Pine condPhaKhungLon
    public const string PhaKhungLonFired       = "PHA_KHUNG_LON_FIRED";
    public const string PhaKhungLonNoBarClose  = "PHA_KHUNG_LON_NO_BAR_CLOSE";
    public const string PhaKhungLonNoTransition = "PHA_KHUNG_LON_NO_TRANSITION";
}
