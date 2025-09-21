#nullable enable  
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TwinFx.Models
{
    internal class CarModel
    { // Identificación básica  
        [JsonPropertyName("stockNumber")]
        public string? StockNumber { get; set; }

        [JsonPropertyName("make")]
        [Required]
        public string Make { get; set; } = null!;

        [JsonPropertyName("model")]
        [Required]
        public string Model { get; set; } = null!;

        [JsonPropertyName("year")]
        [Required]
        public int Year { get; set; }

        [JsonPropertyName("trim")]
        public string? Trim { get; set; }

        [JsonPropertyName("subModel")]
        public string? SubModel { get; set; }

        [JsonPropertyName("bodyStyle")]
        public BodyStyle? BodyStyle { get; set; }

        [JsonPropertyName("doors")]
        public int? Doors { get; set; }

        [JsonPropertyName("licensePlate")]
        [Required]
        public string LicensePlate { get; set; } = null!;

        [JsonPropertyName("plateState")]
        public string? PlateState { get; set; }

        [JsonPropertyName("vin")]
        public string? Vin { get; set; }

        // Especificaciones Técnicas  
        [JsonPropertyName("transmission")]
        public TransmissionType? Transmission { get; set; }

        [JsonPropertyName("drivetrain")]
        public DrivetrainType? Drivetrain { get; set; }

        [JsonPropertyName("fuelType")]
        public FuelType? FuelType { get; set; }

        [JsonPropertyName("engineDescription")]
        public string? EngineDescription { get; set; }

        [JsonPropertyName("cylinders")]
        public int? Cylinders { get; set; }

        [JsonPropertyName("engineDisplacementLiters")]
        public decimal? EngineDisplacementLiters { get; set; }

        [JsonPropertyName("mileage")]
        public long? Mileage { get; set; }

        [JsonPropertyName("mileageUnit")]
        public MileageUnit? MileageUnit { get; set; }

        [JsonPropertyName("odometerStatus")]
        public OdometerStatus? OdometerStatus { get; set; }

        // Colores y Apariencia  
        [JsonPropertyName("exteriorColor")]
        public string? ExteriorColor { get; set; }

        [JsonPropertyName("interiorColor")]
        public string? InteriorColor { get; set; }

        [JsonPropertyName("upholstery")]
        public string? Upholstery { get; set; }

        // Estado y Condición  
        [JsonPropertyName("condition")]
        public ConditionType? Condition { get; set; }

        [JsonPropertyName("stockStatus")]
        public StockStatus? StockStatus { get; set; }

        [JsonPropertyName("hasOpenRecalls")]
        public bool HasOpenRecalls { get; set; }

        [JsonPropertyName("hasAccidentHistory")]
        public bool HasAccidentHistory { get; set; }

        [JsonPropertyName("isCertifiedPreOwned")]
        public bool IsCertifiedPreOwned { get; set; }

        // Fechas y Adquisición  
        [JsonPropertyName("dateAcquired")]
        public DateTime? DateAcquired { get; set; } // ISO dates  

        [JsonPropertyName("dateListed")]
        public DateTime? DateListed { get; set; } // ISO dates  

        [JsonPropertyName("acquisitionSource")]
        public string? AcquisitionSource { get; set; }

        // Ubicación  
        [JsonPropertyName("addressComplete")]
        public string? AddressComplete { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("postalCode")]
        public string? PostalCode { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("latitude")]
        public decimal? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public decimal? Longitude { get; set; }

        [JsonPropertyName("parkingLocation")]
        public string? ParkingLocation { get; set; }

        // Información Financiera  
        [JsonPropertyName("listPrice")]
        public decimal? ListPrice { get; set; }

        [JsonPropertyName("currentPrice")]
        public decimal? CurrentPrice { get; set; }

        [JsonPropertyName("estimatedTax")]
        public decimal? EstimatedTax { get; set; }

        [JsonPropertyName("estimatedRegistrationFee")]
        public decimal? EstimatedRegistrationFee { get; set; }

        [JsonPropertyName("dealerProcessingFee")]
        public decimal? DealerProcessingFee { get; set; }

        // Financiamiento  
        [JsonPropertyName("monthlyPayment")]
        public decimal? MonthlyPayment { get; set; }

        [JsonPropertyName("apr")]
        public decimal? Apr { get; set; }

        [JsonPropertyName("termMonths")]
        public int? TermMonths { get; set; }

        [JsonPropertyName("downPayment")]
        public decimal? DownPayment { get; set; }

        // Características  
        [JsonPropertyName("standardFeatures")]
        public List<string> StandardFeatures { get; } = new List<string>();

        [JsonPropertyName("optionalFeatures")]
        public List<string> OptionalFeatures { get; } = new List<string>();

        [JsonPropertyName("safetyFeatures")]
        public List<string> SafetyFeatures { get; } = new List<string>();

        // Título  
        [JsonPropertyName("titleBrand")]
        public TitleBrand? TitleBrand { get; set; }

        [JsonPropertyName("hasLien")]
        public bool HasLien { get; set; }

        [JsonPropertyName("lienHolder")]
        public string? LienHolder { get; set; }

        [JsonPropertyName("lienAmount")]
        public decimal? LienAmount { get; set; }

        [JsonPropertyName("titleState")]
        public string? TitleState { get; set; }

        // Garantía  
        [JsonPropertyName("warrantyType")]
        public string? WarrantyType { get; set; }

        [JsonPropertyName("warrantyStart")]
        public DateTime? WarrantyStart { get; set; } // ISO date  

        [JsonPropertyName("warrantyEnd")]
        public DateTime? WarrantyEnd { get; set; } // ISO date  

        [JsonPropertyName("warrantyProvider")]
        public string? WarrantyProvider { get; set; }

        // Multimedia  
        [JsonPropertyName("photos")]
        public List<Photo> Photos { get; } = new List<Photo>();

        [JsonPropertyName("videoUrl")]
        public string? VideoUrl { get; set; }

        // Notas  
        [JsonPropertyName("internalNotes")]
        public string? InternalNotes { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        // Compatibilidad / estado (propio, financiado, arrendado, vendido)  
        [JsonPropertyName("estado")]
        public OwnershipStatus? Estado { get; set; }

        // Metadatos  
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // ISO date  

        [JsonPropertyName("createdBy")]
        public string? CreatedBy { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }

        [JsonPropertyName("updatedBy")]
        public string? UpdatedBy { get; set; }
    }

    /// <summary>  
    /// Representa una foto/imagen asociada al vehículo.  
    /// </summary>  
    public class Photo
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = null!;

        [JsonPropertyName("caption")]
        public string? Caption { get; set; }

        [JsonPropertyName("isPrimary")]
        public bool IsPrimary { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("uploadedAt")]
        public DateTime? UploadedAt { get; set; }
    }

    // Enums (serializados como strings)  
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BodyStyle
    {
        Sedan,
        Coupe,
        Hatchback,
        Wagon,
        SUV,
        Truck,
        Van,
        Convertible,
        Other
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TransmissionType
    {
        Automatic,
        Manual,
        CVT,
        DualClutch,
        Other
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DrivetrainType
    {
        FWD,
        RWD,
        AWD,
        FourWheelDrive
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FuelType
    {
        Gasoline,
        Diesel,
        Hybrid,
        PlugInHybrid,
        Electric,
        FlexFuel,
        Other
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MileageUnit
    {
        Km,
        Mi
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OdometerStatus
    {
        Actual,
        ExceedsMechanicalLimits,
        NotActual
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ConditionType
    {
        New,
        LikeNew,
        Excellent,
        VeryGood,
        Good,
        Fair,
        Salvage
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StockStatus
    {
        Available,
        PendingSale,
        OnHold,
        Sold,
        Transferred,
        InTransit
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TitleBrand
    {
        Clean,
        Salvage,
        Rebuilt,
        Flood,
        Lemon,
        OdometerProblem,
        Junk
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OwnershipStatus
    {
        Propio,
        Financiado,
        Arrendado,
        Vendido
    }
} 
