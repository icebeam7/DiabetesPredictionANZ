using System;
using System.IO;
using System.Linq;
using System.Data.SqlClient;

using Microsoft.ML;
using Microsoft.ML.Data;

using Microsoft.Extensions.Configuration;

using DiabetesPrediction.Models;

namespace DiabetesPrediction
{
    class Program
    {
        private static string GetDbConnection()
        {
            var builder = new ConfigurationBuilder()
                                .SetBasePath(Directory.GetCurrentDirectory())
                                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            return builder.Build().GetConnectionString("DbConnection");
        }

        static void Main(string[] args)
        {
            var context = new MLContext();

            var loader = context.Data.CreateDatabaseLoader<Patient>();

            var connectionString = GetDbConnection();
            var sqlCommand = "Select CAST(Id as REAL) as Id, Pregnancies, Glucose, BloodPressure, SkinThickness, Insulin, BMI, DiabetesPedigreeFunction, Age, CAST(Output as REAL) as Output From Patient";
            var dbSource = new DatabaseSource(SqlClientFactory.Instance, connectionString, sqlCommand);

            Console.WriteLine("Loading data from database...");

            var data = loader.Load(dbSource);
            var set = context.Data.TrainTestSplit(data, testFraction: 0.2);
            var trainingData = set.TrainSet;
            var testData = set.TestSet;

            Console.WriteLine("Preparing training operations...");
            var pipeline = context.Transforms
                .Conversion.MapValueToKey(outputColumnName: "Label", inputColumnName: "Output")
                .Append(context.Transforms.Concatenate("Features", "Pregnancies", "Glucose", "BloodPressure", "SkinThickness", "Insulin", "BMI", "DiabetesPedigreeFunction", "Age"))
                .Append(context.MulticlassClassification.Trainers.OneVersusAll(
                    context.BinaryClassification.Trainers.SdcaLogisticRegression("Label", "Features", maximumNumberOfIterations: 10))
                .Append(context.Transforms.Conversion.MapKeyToValue("PredictedLabel")));

            Console.WriteLine("=============== Starting 10 fold cross validation ===============");
            var crossValResults = context.MulticlassClassification.CrossValidate(data: trainingData, estimator: pipeline, numberOfFolds: 10, labelColumnName: "Label");
            var metricsInMultipleFolds = crossValResults.Select(r => r.Metrics);
            var microAccuracyValues = metricsInMultipleFolds.Select(m => m.MicroAccuracy);
            var microAccuracyAverage = microAccuracyValues.Average();
            var macroAccuracyValues = metricsInMultipleFolds.Select(m => m.MacroAccuracy);
            var macroAccuracyAverage = macroAccuracyValues.Average();
            var logLossValues = metricsInMultipleFolds.Select(m => m.LogLoss);
            var logLossAverage = logLossValues.Average();
            var logLossReductionValues = metricsInMultipleFolds.Select(m => m.LogLossReduction);
            var logLossReductionAverage = logLossReductionValues.Average(); Console.WriteLine($"*************************************************************************************************************");

            Console.WriteLine($"*       Metrics Multi-class Classification model      ");
            Console.WriteLine($"*------------------------------------------------------------------------------------------------------------");
            Console.WriteLine($"*       Average MicroAccuracy:    {microAccuracyAverage:0.###} ");
            Console.WriteLine($"*       Average MacroAccuracy:    {macroAccuracyAverage:0.###} ");
            Console.WriteLine($"*       Average LogLoss:          {logLossAverage:#.###} ");
            Console.WriteLine($"*       Average LogLossReduction: {logLossReductionAverage:#.###} ");
            Console.WriteLine($"*************************************************************************************************************");

            //Now we need to train the model using below code    
            Console.WriteLine($"Training process is starting. {DateTime.Now.ToLongTimeString()}");
            var model = pipeline.Fit(trainingData);
            Console.WriteLine($"Training process has finished. {DateTime.Now.ToLongTimeString()}");

            //

            var testPatients = context.Data.CreateEnumerable<Patient>(testData, reuseRowObject: true);
            Console.WriteLine($"Test Set: {testPatients.Count()} patients");

            Console.WriteLine("Saving the model");
            context.Model.Save(model, trainingData.Schema, "MLModel.zip");

            var predictionEngine = context.Model.CreatePredictionEngine<Patient, DiabetesMLPrediction>(model);

            var patient1 = new Patient()
            {
                Age = 50,
                BloodPressure = 72,
                BMI = 33.6f,
                DiabetesPedigreeFunction = 0.627f,
                Glucose = 148,
                Insulin = 200,
                Pregnancies = 6,
                SkinThickness = 35,
                Output = 0
            };

            var patient2 = new Patient()
            {
                Age = 31,
                BloodPressure = 66,
                BMI = 26.6f,
                DiabetesPedigreeFunction = 0.351f,
                Glucose = 85,
                Insulin = 200,
                Pregnancies = 1,
                SkinThickness = 29,
                Output = 0
            };

            var prediction1 = predictionEngine.Predict(patient1);
            Console.WriteLine($"Diabetes? {prediction1.Score[0]} | Prediction: {(Convert.ToBoolean(prediction1.Prediction) ? "Yes" : "No")} ");

            var prediction2 = predictionEngine.Predict(patient2);
            Console.WriteLine($"Diabetes? {prediction2.Score[0]} | Prediction: {(Convert.ToBoolean(prediction2.Prediction) ? "Yes" : "No")} ");
        }
    }
}
