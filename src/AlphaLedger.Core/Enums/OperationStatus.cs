namespace AlphaLedger.Core.Enums;

public enum OperationStatus
{
    NotStarted = 0,
    InProgress = 1,
    WaitingForDocuments = 2,
    UnderReview = 3,
    ReadyForDelivery = 4,
    SubmittedOrDelivered = 5,
    Completed = 6,
    Cancelled = 7
}
