namespace DARI_API.Models
{
    public enum AccountStatus
    {
        Pending,
        Active,
        Suspended,
        Banned
    }

    public enum UserType
    {
        Customer,
        Lister,
        Admin
    }

    public enum CustomerType
    {
        Buyer,
        Renter
    }

    public enum ListerType
    {
        Individual,
        Agent
    }

    public enum PropertyType
    {
        Apartment,
        Villa,
        Townhouse,
        Studio,
        Duplex,
        Penthouse
    }

    public enum ListingType
    {
        ForSale,
        ForRent
    }

    public enum ListingKind
    {
        Residential = 0,
        Commercial  = 1
    }

    public enum PaymentMethod
    {
        Cash         = 0,
        Installments = 1,
        Both         = 2
    }

    public enum CompletionStatus
    {
        Ready   = 0,
        OffPlan = 1
    }

    public enum ComplaintReason
    {
        Spam                 = 0,
        ScamOrFraud          = 1,
        IncorrectInfo        = 2,
        AlreadySoldOrRented  = 3,
        OffensiveContent     = 4,
        Duplicate            = 5,
        Other                = 6
    }

    public enum ComplaintStatus
    {
        Open        = 0,
        Reviewed    = 1,
        Dismissed   = 2,
        ActionTaken = 3
    }

    public enum ListingStatus
    {
        Draft,
        Active,
        Archived,
        Sold,
        Rented,
        Pending
    }

    public enum InquiryStatus
    {
        Pending,
        Responded,
        Closed
    }

    public enum NotificationType
    {
        NewMessage,
        InquiryResponse,
        ListingApproved,
        ListingRejected,
        NewMatch
    }
    public enum ViewSource
    {
        Search,
        Direct,
        Saved,
        Map
    }
}
