namespace DARI_API.Models
{
    public enum AccountStatus
    {
        Pending,
        Active,
        Suspended
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
        Studio
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
