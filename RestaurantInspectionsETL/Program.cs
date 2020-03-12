using System;
using System.IO;
using Microsoft.Spark.Sql;
using static Microsoft.Spark.Sql.Functions;

namespace RestaurantInspectionsETL
{
    class Program
    {
        static void Main(string[] args)
        {
            // Define columns to remove
            string[] dropCols = new string[]
            {
                "CAMIS",
                "CUISINE DESCRIPTION",
                "VIOLATION DESCRIPTION",
                "BORO",
                "BUILDING",
                "STREET",
                "ZIPCODE",
                "PHONE",
                "ACTION",
                "GRADE DATE",
                "RECORD DATE",
                "Latitude",
                "Longitude",
                "Community Board",
                "Council District",
                "Census Tract",
                "BIN",
                "BBL",
                "NTA"
            };
            
            // Create SparkSession
            var sc =
                SparkSession
                    .Builder()
                    .AppName("Restaurant_Inspections_ETL")
                    .GetOrCreate();

            // Load data
            DataFrame df =
                sc
                .Read()
                .Option("header", "true")
                .Option("inferSchema", "true")
                .Csv("Data/NYC-Restaurant-Inspections.csv");

            //Remove columns and missing values
            DataFrame cleanDf =
                df
                    .Drop(dropCols)
                    .WithColumnRenamed("INSPECTION DATE","INSPECTIONDATE")
                    .WithColumnRenamed("INSPECTION TYPE","INSPECTIONTYPE")
                    .WithColumnRenamed("CRITICAL FLAG","CRITICALFLAG")
                    .WithColumnRenamed("VIOLATION CODE","VIOLATIONCODE")
                    .Na()
                    .Drop();

            // Encode CRITICAL FLAG column
            DataFrame labeledFlagDf =
                cleanDf
                    .WithColumn("CRITICALFLAG",
                        When(Col("CRITICALFLAG") == "Y",1)
                        .Otherwise(0));
            
             // Aggregate violations by business and inspection
            DataFrame groupedDf =
                labeledFlagDf
                    .GroupBy("DBA", "INSPECTIONDATE", "INSPECTIONTYPE", "CRITICALFLAG", "SCORE", "GRADE")
                    .Agg(
                        CollectSet(Col("VIOLATIONCODE")).Alias("CODES"),
                        Sum("CRITICALFLAG").Alias("FLAGS"))
                    .Drop("DBA", "INSPECTIONDATE")
                    .WithColumn("CODES", ArrayJoin(Col("CODES"), ","))
                    .Select("INSPECTIONTYPE", "CODES", "FLAGS", "SCORE", "GRADE");

            // Split into graded and ungraded DataFrames
            DataFrame gradedDf =
                groupedDf
                .Filter(
                    Col("GRADE") == "A" |
                    Col("GRADE") == "B" |
                    Col("GRADE") == "C" );

            DataFrame ungradedDf =
                groupedDf
                    .Filter(
                        Col("GRADE") != "A" &
                        Col("GRADE") != "B" &
                        Col("GRADE") != "C" );           

            // Save DataFrames
            var timestamp = ((DateTimeOffset) DateTime.UtcNow).ToUnixTimeSeconds().ToString();

            var saveDirectory = Path.Join("Output",timestamp);

            if(!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            gradedDf.Write().Mode(SaveMode.Overwrite).Csv(Path.Join(saveDirectory,"Graded"));

            ungradedDf.Write().Mode(SaveMode.Overwrite).Csv(Path.Join(saveDirectory,"Ungraded"));
        }
    }
}
