using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

using Soriana.PPS.Common.Constants;
using Soriana.PPS.Common.DTO.Common;
using Soriana.PPS.Common.DTO.Cybersource.Payments;
using Soriana.PPS.POS.PendingCancellation.Services;
using Soriana.PPS.POS.PendingCancellation.Constants;
using Soriana.PPS.Common.DTO.ClosureOrder;

namespace Soriana.PPS.POS.PendingCancellation
{
    public class PendingCancellationFunction
    {
        #region Private Fields
        private readonly ILogger<PendingCancellationFunction> _Logger;
        private readonly IPendingCancellationService _PendingCancellationService;
        #endregion

        #region Constructor
        public PendingCancellationFunction(ILogger<PendingCancellationFunction> logger,
                                           IPendingCancellationService pendingCancellationService)
        {
            _Logger = logger;
            _PendingCancellationService = pendingCancellationService;
        }
        #endregion

        #region Public Methods
        [FunctionName(PendingCancellationConstants.PENDING_CANCELLATION_FUNCTION_NAME)]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest request)
        {
            try
            {
                _Logger.LogInformation(string.Format(FunctionAppConstants.FUNCTION_EXECUTING_MESSAGE, PendingCancellationConstants.PENDING_CANCELLATION_FUNCTION_NAME));
                if (!request.Body.CanSeek)
                    throw new Exception(JsonConvert.SerializeObject(new BusinessResponse() { StatusCode = (int)HttpStatusCode.BadRequest, Description = HttpStatusCode.BadRequest.ToString(), DescriptionDetail = PendingCancellationConstants.PENDING_CANCELLATION_NO_CONTENT_REQUEST, ContentRequest = null }));
                request.Body.Position = 0;
                string jsonPaymentOrderProcessRequest = await new StreamReader(request.Body).ReadToEndAsync();

                PendingCancellationRequest paymentOrderProcessRequest = JsonConvert.DeserializeObject<PendingCancellationRequest>(jsonPaymentOrderProcessRequest);

                string Response = await _PendingCancellationService.PendingCancellation(paymentOrderProcessRequest.OrderID);

                return new OkObjectResult(Response);
            }
            catch (BusinessException ex)
            {
                _Logger.LogError(ex, PendingCancellationConstants.PENDING_CANCELLATION_FUNCTION_NAME);
                return new BadRequestObjectResult(ex);
            }
            catch (Exception ex)
            {
                _Logger.LogError(ex, PendingCancellationConstants.PENDING_CANCELLATION_FUNCTION_NAME);
                return new BadRequestObjectResult(new BusinessResponse()
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Description = string.Concat(HttpStatusCode.InternalServerError.ToString(), CharactersConstants.ESPACE_CHAR, CharactersConstants.HYPHEN_CHAR, CharactersConstants.ESPACE_CHAR, PendingCancellationConstants.PENDING_CANCELLATION_FUNCTION_NAME),
                    DescriptionDetail = ex,
                    ContentRequest = ""
                });
            }
        }
        #endregion
    }
}
