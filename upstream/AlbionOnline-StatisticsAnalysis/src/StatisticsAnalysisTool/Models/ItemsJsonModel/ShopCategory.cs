using StatisticsAnalysisTool.Common.Converters;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StatisticsAnalysisTool.Models.ItemsJsonModel;

public class ShopCategory
{
    [JsonPropertyName("@id")]
    public string Id { get; set; }
    [JsonPropertyName("@value")]
    public string Value { get; set; }

    [JsonPropertyName("shopsubcategory")]
    [JsonConverter(typeof(SingleOrArrayConverter<ShopSubCategory1>))]
    public List<ShopSubCategory1> ShopSubCategory { get; set; }
}