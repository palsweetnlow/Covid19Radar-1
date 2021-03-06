using Covid19Radar.Api.DataAccess;
using Covid19Radar.Api.Models;
using Covid19Radar.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;

namespace Covid19Radar.Api
{
    public class DiagnosisApi
    {
        private readonly IDiagnosisRepository DiagnosisRepository;
        private readonly ITemporaryExposureKeyRepository TekRepository;
        private readonly IValidationUserService Validation;
        private readonly IDeviceValidationService DeviceCheck;
        private readonly ILogger<DiagnosisApi> Logger;
        private readonly string[] SupportRegions;

        public DiagnosisApi(
            IConfiguration config,
            IDiagnosisRepository diagnosisRepository,
            ITemporaryExposureKeyRepository tekRepository,
            IValidationUserService validation,
            IDeviceValidationService deviceCheck,
            ILogger<DiagnosisApi> logger)
        {
            DiagnosisRepository = diagnosisRepository;
            TekRepository = tekRepository;
            Validation = validation;
            DeviceCheck = deviceCheck;
            Logger = logger;
            SupportRegions = config.SupportRegions();
        }

        [FunctionName(nameof(DiagnosisApi))]
        public async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "diagnosis")] HttpRequest req)
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var diagnosis = JsonConvert.DeserializeObject<DiagnosisSubmissionParameter>(requestBody);

            // payload valid
            if (!diagnosis.IsValid())
            {
                return new BadRequestErrorMessageResult("Invalid parameter");
            }

            if (!SupportRegions.Contains(diagnosis.Region))
            {
                return new BadRequestErrorMessageResult("Regions not supported.");
            }

            // validation
            var validationResult = await Validation.ValidateAsync(req, diagnosis);
            if (!validationResult.IsValid)
            {
                return validationResult.ErrorActionResult;
            }

            // Device validation
            if (false == await DeviceCheck.Validation(diagnosis))
            {
                return new BadRequestErrorMessageResult("Invalid Device");
            }

            var timestamp = DateTimeOffset.UtcNow;
            var keys = diagnosis.Keys.Select(_ => _.ToModel(diagnosis, (ulong)timestamp.ToUnixTimeSeconds())).ToArray();

            await DiagnosisRepository.SubmitDiagnosisAsync(
                diagnosis.SubmissionNumber,
                timestamp,
                diagnosis.UserUuid,
                keys);

            foreach (var k in keys)
            {
                await TekRepository.UpsertAsync(k);
            }

            return new NoContentResult();
        }
    }
}
