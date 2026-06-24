using System;

[Serializable]
public class ProductScanResult
{
    public bool barcodeFound;
    public string barcodeValue;

    public bool hasDatabaseMatch;

    public string productName;
    public string brand;
    public string quantity;
    public string ingredients;
    public string allergens;
    public string expiryDate;

    public string rawOcrText;
    public string sourceSummary;

    // NOU
    public string frontRawText;
    public string backRawText;
    public string finalSummary;

    public bool HasUsefulInfo()
    {
        return !string.IsNullOrWhiteSpace(productName) ||
               !string.IsNullOrWhiteSpace(brand) ||
               !string.IsNullOrWhiteSpace(quantity) ||
               !string.IsNullOrWhiteSpace(ingredients) ||
               !string.IsNullOrWhiteSpace(allergens) ||
               !string.IsNullOrWhiteSpace(expiryDate) ||
               !string.IsNullOrWhiteSpace(rawOcrText) ||
               !string.IsNullOrWhiteSpace(frontRawText) ||
               !string.IsNullOrWhiteSpace(backRawText);
    }
}