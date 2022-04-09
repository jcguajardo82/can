using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using Soriana.PPS.Common.DTO.Salesforce;
using Soriana.PPS.DataAccess.PaymentProcess;
using Soriana.PPS.Common.DTO.ProgramaLealtad;
using Soriana.PPS.Common.DTO.Cancelacion;
using RestSharp;
using Soriana.PPS.Common.DTO.ClosureOrder;

namespace Soriana.PPS.POS.PendingCancellation.Services
{
    public class PendingCancellationService : IPendingCancellationService
    {
        #region Private Fields
        private readonly ILogger<PendingCancellationService> _Logger;
        private readonly IPaymentProcessContext _PaymentProcessContext;
        #endregion

        #region Constructors
        public PendingCancellationService(ILogger<PendingCancellationService> logger,
                                          IPaymentProcessContext paymentProcessContext)
        {
            _Logger = logger;
            _PaymentProcessContext = paymentProcessContext;
        }
        #endregion

        #region Public Methods
        public async Task<string> PendingCancellation(string OrderID)
        {
            #region Definiciones
            bool flagLoyalty = false;
            bool flagOmonel = false;
            bool flagPaypal = false;
            string AccountNumberOmonel = string.Empty;
            string PPSCancellationFunction = Environment.GetEnvironmentVariable("PPSCancellationFunction");
            string MBSWebAPI = Environment.GetEnvironmentVariable("MBSWebAPI");
            PaymentOrderProcessRequest paymentOrder = new PaymentOrderProcessRequest();
            string Response = string.Empty;
            #endregion

            #region Trace Payment
            TracePayment tracePaymentRequestPending = new TracePayment
            {
                method = "CANC_PendingCancellation_Ini",
                order = OrderID,
                request = OrderID,
                response = ""
            };

            await SaveLogTracePayment(tracePaymentRequestPending);
            #endregion

            #region Datos Order
            var TransactionByOrder = await _PaymentProcessContext.GetOrder(OrderID);

      
            if(TransactionByOrder.Count > 0)
            {
                string jsonOrder = TransactionByOrder[0];

                paymentOrder = JsonConvert.DeserializeObject<PaymentOrderProcessRequest>(jsonOrder);

                if (paymentOrder.PaymentType == "WALLET")
                    flagPaypal = true;
            }
            else
            {
                var TransactionByOrderPaypal = await _PaymentProcessContext.GetTransactionbyOrderPaypal(OrderID);

                string jsonOrder = TransactionByOrderPaypal[0].ToString();

                paymentOrder = JsonConvert.DeserializeObject<PaymentOrderProcessRequest>(jsonOrder);

                if (paymentOrder.PaymentType == "WALLET")
                    flagPaypal = true;
            }
            #endregion

            #region Validacion Montos con Programa de Lealtad             
            if (Convert.ToInt32(paymentOrder.CustomerLoyaltyRedeemPoints) > 0 || Convert.ToDecimal(paymentOrder.CustomerLoyaltyRedeemMoney) > 0 || Convert.ToDecimal(paymentOrder.CustomerLoyaltyRedeemElectronicMoney) > 0)
                flagLoyalty = true;
            #endregion

            #region  Paypal
            if (flagPaypal == true)
            {
                Response = await SaldosPaypal(paymentOrder, flagLoyalty);

                return Response;
            }
            #endregion

            #region Validate Payment Omonel
            var ValidatePaymentOmonel = await _PaymentProcessContext.ValidatePaymentOmonel(paymentOrder.PaymentToken);

            foreach (var omonel in ValidatePaymentOmonel)
            {
                if (paymentOrder.PaymentToken == omonel.ClientToken)
                {
                    flagOmonel = true;
                    AccountNumberOmonel = omonel.CardAccountNumber;
                    break;
                }
            }
            #endregion

            if(flagOmonel == true)
            {
                #region Omonel
                var AccountOmonel = await _PaymentProcessContext.GetAccountNumberOmonel(paymentOrder.PaymentToken);
                AccountNumberOmonel = AccountOmonel[0].CardAccountNumber.ToString();

                var ResponseOmonel = await SaldosOmonel(paymentOrder, flagLoyalty, "CANCELACION", AccountNumberOmonel);

                Response = ResponseOmonel.ToString();
                #endregion
            }
            else
            {
                #region Cyber
                if (flagLoyalty == false)
                {
                    Response = await OrderCancellation(PPSCancellationFunction, paymentOrder.OrderReferenceNumber);
                    //var Cancellation = await OrderCancellation(PPSCancellationFunction, paymentOrder);
                }
                else
                {
                    Saldo_Req LoyaltyReq = new Saldo_Req
                    {
                        Id_Cve_GUID = paymentOrder.CustomerId,
                        Id_Cve_Orden = paymentOrder.OrderReferenceNumber + "10",
                        Id_Cve_TokenCta = paymentOrder.CustomerLoyaltyCardId,
                        Cant_Puntos = int.Parse(paymentOrder.CustomerLoyaltyRedeemPoints),
                        Imp_Vta = decimal.Parse(paymentOrder.OrderAmount),
                        Imp_Comp = 0,
                        Imp_DE = decimal.Parse(paymentOrder.CustomerLoyaltyRedeemElectronicMoney),
                        Imp_Efvo = decimal.Parse(paymentOrder.CustomerLoyaltyRedeemMoney),
                        Imp_Cred = 0,
                        Cve_Operacion = "DEVOLUCION",   //DISMINUCION
                        Cve_Accion = "AUMENTA"
                    };

                    var response = await POST_LoyaltyAccount(LoyaltyReq);  

                    if(response.Bit_Error == false)
                    {
                        Response = await OrderCancellation(PPSCancellationFunction, paymentOrder.OrderReferenceNumber);
                    }                  
                }
                #endregion
            }

            #region Trace Payment
            TracePayment tracePaymentResponsePending = new TracePayment
            {
                method = "CANC_PendingCancellation_Fin",
                order = OrderID,
                request = OrderID,
                response = Response
            };
            await SaveLogTracePayment(tracePaymentResponsePending);
            #endregion

            return Response;
        }
        #endregion

        #region Private Methods      
        private async Task<string> SaldosOmonel(PaymentOrderProcessRequest paymentOrderProcessRequest, bool flagLoyalty, string Accion, string AccountOmonel)
        {
            Guid g = Guid.NewGuid();

            string ResponseOmonel = string.Empty;

            if (Accion == "CANCELACION")
            {
                if (flagLoyalty == true)
                {
                    #region Programa de Lealtad
                    Saldo_Req LoyaltyReq = new Saldo_Req
                    {
                        Id_Cve_GUID = paymentOrderProcessRequest.CustomerId,
                        Id_Cve_Orden = paymentOrderProcessRequest.OrderReferenceNumber + "10",
                        Id_Cve_TokenCta = paymentOrderProcessRequest.CustomerLoyaltyCardId,
                        Cant_Puntos = int.Parse(paymentOrderProcessRequest.CustomerLoyaltyRedeemPoints),
                        Imp_Vta = decimal.Parse(paymentOrderProcessRequest.OrderAmount),
                        Imp_Comp = 0,
                        Imp_DE = decimal.Parse(paymentOrderProcessRequest.CustomerLoyaltyRedeemElectronicMoney),
                        Imp_Efvo = decimal.Parse(paymentOrderProcessRequest.CustomerLoyaltyRedeemMoney),
                        Imp_Cred = 0,
                        Cve_Operacion = "DEVOLUCION",
                        Cve_Accion = "AUMENTA"
                    };

                    var response = await POST_LoyaltyAccount(LoyaltyReq);
                    #endregion

                    if(response.Bit_Error == false)
                    {
                        #region Cancelacion OMONEL
                        decimal TotalOrder = decimal.Parse(paymentOrderProcessRequest.OrderAmount) - decimal.Parse(paymentOrderProcessRequest.CustomerLoyaltyRedeemElectronicMoney) - decimal.Parse(paymentOrderProcessRequest.CustomerLoyaltyRedeemMoney);

                        Omonel_Req reqOmonel = new Omonel_Req
                        {
                            Id_Cve_Orden = paymentOrderProcessRequest.OrderReferenceNumber + "10",
                            Id_Cve_GUID = g.ToString(),
                            Id_Num_Cta = AccountOmonel,
                            Cve_Acceso = paymentOrderProcessRequest.PaymentCardNIP,
                            Imp_Comp = TotalOrder.ToString(),          //paymentOrderProcessRequest.OrderAmount,
                            Cve_Operacion = "CANCELACION",
                            Cve_Accion = "AUMENTA"
                        };

                        ResponseOmonel = await POST_Omonel(reqOmonel);
                        #endregion
                    }
                }
                else
                {
                    #region Cancelacion OMONEL
                    Omonel_Req reqOmonel = new Omonel_Req
                    {
                        Id_Cve_Orden = paymentOrderProcessRequest.OrderReferenceNumber + "10",
                        Id_Cve_GUID = g.ToString(),
                        Id_Num_Cta = AccountOmonel,
                        Cve_Acceso = paymentOrderProcessRequest.PaymentCardNIP,
                        Imp_Comp = paymentOrderProcessRequest.OrderAmount,
                        Cve_Operacion = "CANCELACION",
                        Cve_Accion = "AUMENTA"
                    };

                    ResponseOmonel = await POST_Omonel(reqOmonel);
                    #endregion
                }
            }

            return ResponseOmonel;
        }

        private async Task<string> SaldosPaypal(PaymentOrderProcessRequest paymentOrderProcess, bool flagLoyalty)
        {
            try
            {
                string Response = string.Empty;

                if(flagLoyalty == true)
                {
                    Saldo_Req LoyaltyReq = new Saldo_Req
                    {
                        Id_Cve_GUID = paymentOrderProcess.CustomerId,
                        Id_Cve_Orden = paymentOrderProcess.OrderReferenceNumber + "10",
                        Id_Cve_TokenCta = paymentOrderProcess.CustomerLoyaltyCardId,
                        Cant_Puntos = int.Parse(paymentOrderProcess.CustomerLoyaltyRedeemPoints),
                        Imp_Vta = decimal.Parse(paymentOrderProcess.OrderAmount),
                        Imp_Comp = 0,
                        Imp_DE = decimal.Parse(paymentOrderProcess.CustomerLoyaltyRedeemElectronicMoney),
                        Imp_Efvo = decimal.Parse(paymentOrderProcess.CustomerLoyaltyRedeemMoney),
                        Imp_Cred = 0,
                        Cve_Operacion = "DEVOLUCION",   //DISMINUCION
                        Cve_Accion = "AUMENTA"
                    };

                    var responseLoyalty = await POST_LoyaltyAccount(LoyaltyReq);

                    decimal TotalOrder = decimal.Parse(paymentOrderProcess.OrderAmount) - decimal.Parse(paymentOrderProcess.CustomerLoyaltyRedeemElectronicMoney) - decimal.Parse(paymentOrderProcess.CustomerLoyaltyRedeemMoney);

                    if (responseLoyalty.Bit_Error == false)
                    {
                        #region Trace Payment
                        TracePayment tracePaymentReqPaypal = new TracePayment
                        {
                            method = "CANC_PendingCancellation_Paypal_Ini",
                            order = paymentOrderProcess.OrderReferenceNumber,
                            request = "Cancelacion?No_Order=" + paymentOrderProcess.OrderReferenceNumber + "&amount=" + TotalOrder.ToString(),
                            response = ""
                        };

                        await SaveLogTracePayment(tracePaymentReqPaypal);
                        #endregion

                        Response = RestClient_Paypal("Cancelacion", paymentOrderProcess.OrderReferenceNumber, TotalOrder.ToString());

                        #region Trace Payment
                        TracePayment tracePaymentResPaypal = new TracePayment
                        {
                            method = "CANC_PendingCancellation_Paypal_Fin",
                            order = paymentOrderProcess.OrderReferenceNumber,
                            request =  "Cancelacion?No_Order=" + paymentOrderProcess.OrderReferenceNumber + "&amount=" + TotalOrder.ToString(),
                            response = Response.ToString()
                        };

                        await SaveLogTracePayment(tracePaymentResPaypal);
                        #endregion
                    }
                }
                else
                {
                    Response = RestClient_Paypal("Cancelacion", paymentOrderProcess.OrderReferenceNumber, paymentOrderProcess.OrderAmount.ToString());
                }

                return Response;
            }
            catch (Exception ex)
            {
                //xmlResponse = await _ResponseXMLService.GetResponseXMLwithError("90", ConfigurationConstants.MENSAJE_COBRO_FALLA + " " + closureOrderGrocery.Numero_Orden, ConfigurationConstants.MENSAJE_COBRO_FALLA + " " + closureOrderGrocery.Numero_Orden);

                throw ex;
            }
        }

        private string RestClient_Paypal(string Function, string OrderID, string amount)
        {
            // CANCELACION https://sorianappspaypalrefundqa.azurewebsites.net/api/PaypalRefund?order=00012992&amount=100.00
            // LIQUIDACION https://sorianappspaypalcapturepayment2.azurewebsites.net/api/PaypalCapturePayment?order=00012992&amount=100.00

            RestClient client = new RestClient();

            string UrlPaypal_Cancel = Environment.GetEnvironmentVariable("PaypalCancellation");
            string UrlPaypal_Capture = Environment.GetEnvironmentVariable("PaypalLiquidacion");



            if (Function == "Cancelacion")
                client = new RestClient(UrlPaypal_Cancel + "?No_Order=" + OrderID + "&amount=" + amount);
            else if (Function == "Capture")
                client = new RestClient(UrlPaypal_Capture + "?No_Order=" + OrderID + "&amount=" + amount);
            else if (Function == "Devolucion")
                client = new RestClient(UrlPaypal_Cancel + "?No_Order=" + OrderID + "&amount=" + amount);


            client.Timeout = -1;
            var request = new RestRequest(Method.POST);
            IRestResponse response = client.Execute(request);
            Console.WriteLine(response.Content);

            return response.Content;
        }

        private async Task<string> OrderCancellation(string UrlFunction, string OrderID)
        {
            #region Trace Payment
            TracePayment tracePaymentReqCyber = new TracePayment
            {
                method = "CANC_PendingCancellation_Cyber_Ini",
                order = OrderID,
                request = UrlFunction + "?order=" + OrderID,
                response = ""
            };

            await SaveLogTracePayment(tracePaymentReqCyber);
            #endregion

            var client = new RestClient(UrlFunction + "?order=" + OrderID);
            client.Timeout = -1;
            var request = new RestRequest(Method.POST);
            IRestResponse response = client.Execute(request);
            Console.WriteLine(response.Content);

            #region Trace Payment
            TracePayment tracePaymentResCyber = new TracePayment
            {
                method = "CANC_PendingCancellation_Cyber_Fin",
                order = OrderID,
                request = UrlFunction + "?order=" + OrderID,
                response = response.Content
            };

            await SaveLogTracePayment(tracePaymentResCyber);
            #endregion

            return response.Content;
        }

        private async Task<Saldo_Res> POST_LoyaltyAccount(Saldo_Req ProgramaLealtadRequest)
        {
            string PPSRequest = JsonConvert.SerializeObject(ProgramaLealtadRequest);
            string MBSWebAPI = System.Environment.GetEnvironmentVariable("MBSWebAPI");
            MBSWebAPI = MBSWebAPI + "ProcesadorPagosSaldo";

            #region Trace Payment
            TracePayment tracePaymentReqLoyalty = new TracePayment
            {
                method = "CANC_PendingCancellation_Loyalty_Ini",
                order = ProgramaLealtadRequest.Id_Cve_Orden,
                request = PPSRequest,
                response = ""
            };

            await SaveLogTracePayment(tracePaymentReqLoyalty);
            #endregion

            #region HTTP 
            FmkTools.RestResponse responseApi = FmkTools.RestClient.RequestRest_1(FmkTools.HttpVerb.POST, MBSWebAPI, null, PPSRequest);
            string jsonResponse = responseApi.message;

            Saldo_Res PPSResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<Saldo_Res>(jsonResponse);
            #endregion

            #region Trace Payment
            TracePayment tracePaymentResLoyalty = new TracePayment
            {
                method = "CANC_PendingCancellation_Loyalty_Fin",
                order = ProgramaLealtadRequest.Id_Cve_Orden,
                request = PPSRequest,
                response = jsonResponse
            };

            await SaveLogTracePayment(tracePaymentResLoyalty);
            #endregion

            return PPSResponse;
        }

        private async Task<string> POST_Omonel(Omonel_Req omonel_Req)
        {
            string PPSRequest = JsonConvert.SerializeObject(omonel_Req);
            string MBSWebAPI = System.Environment.GetEnvironmentVariable("MBSWebAPI");
            MBSWebAPI = MBSWebAPI + "SaldosOmonel";

            #region Trace Payment
            TracePayment tracePaymentReqOmonel = new TracePayment
            {
                method = "CANC_PendingCancellation_Omonel_Ini",
                order = omonel_Req.Id_Cve_Orden,
                request = PPSRequest,
                response = ""
            };

            await SaveLogTracePayment(tracePaymentReqOmonel);
            #endregion

            #region HTTP 
            FmkTools.RestResponse responseApi = FmkTools.RestClient.RequestRest_1(FmkTools.HttpVerb.POST, MBSWebAPI, null, PPSRequest);
            string jsonResponse = responseApi.message;
            #endregion

            #region Trace Payment
            TracePayment tracePaymentResOmonel = new TracePayment
            {
                method = "CANC_PendingCancellation_Omonel_Fin",
                order = omonel_Req.Id_Cve_Orden,
                request = PPSRequest,
                response = jsonResponse
            };

            await SaveLogTracePayment(tracePaymentResOmonel);
            #endregion

            return jsonResponse;
        }

        private async Task SaveLogTracePayment(TracePayment tracePayment)
        {
            await _PaymentProcessContext.SaveLogTracePayment(tracePayment);
        }
        #endregion
    }
}
