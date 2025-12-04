using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Filters;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Web.Http.Hosting;

namespace NestoAPI.Tests.Infrastructure.Filters
{
    [TestClass]
    public class GlobalExceptionFilterTests
    {
        private GlobalExceptionFilter _filter;
        private HttpActionExecutedContext _context;

        [TestInitialize]
        public void Setup()
        {
            _filter = new GlobalExceptionFilter();

            // Configurar el contexto HTTP de prueba
            var config = new HttpConfiguration();
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/test");
            request.Properties[HttpPropertyKeys.HttpConfigurationKey] = config;

            var controllerContext = new HttpControllerContext(config, new HttpRouteData(new HttpRoute()), request);
            var actionContext = new HttpActionContext(controllerContext, new ReflectedHttpActionDescriptor());

            _context = new HttpActionExecutedContext(actionContext, null);
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_ExcepcionGenerica_DevuelveStatusCode500()
        {
            // Arrange
            var genericException = new Exception("Error genérico de prueba");
            _context = new HttpActionExecutedContext(_context.ActionContext, genericException);

            // Act
            _filter.OnException(_context);

            // Assert
            Assert.IsNotNull(_context.Response);
            Assert.AreEqual(HttpStatusCode.InternalServerError, _context.Response.StatusCode);
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_ExcepcionGenerica_ContieneCodigoInternalError()
        {
            // Arrange
            var genericException = new Exception("Error genérico de prueba");
            _context = new HttpActionExecutedContext(_context.ActionContext, genericException);

            // Act
            _filter.OnException(_context);

            // Assert
            var content = _context.Response.Content.ReadAsAsync<Dictionary<string, object>>().Result;
            Assert.IsTrue(content.ContainsKey("error"));
            var error = content["error"] as Newtonsoft.Json.Linq.JObject;
            Assert.AreEqual("INTERNAL_ERROR", error["code"].ToString());
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_ExcepcionGenerica_IncluyeTimestamp()
        {
            // Arrange
            var genericException = new Exception("Error genérico de prueba");
            _context = new HttpActionExecutedContext(_context.ActionContext, genericException);

            // Act
            _filter.OnException(_context);

            // Assert
            var content = _context.Response.Content.ReadAsAsync<Dictionary<string, object>>().Result;
            var error = content["error"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsTrue(error.ContainsKey("timestamp"));
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_NestoBusinessException_DevuelveStatusCodeDeExcepcion()
        {
            // Arrange
            var businessException = new FacturacionException(
                "Error de facturación de prueba",
                "FACTURACION_TEST_ERROR",
                empresa: "1",
                pedido: 12345);
            _context = new HttpActionExecutedContext(_context.ActionContext, businessException);

            // Act
            _filter.OnException(_context);

            // Assert
            Assert.IsNotNull(_context.Response);
            // FacturacionException tiene StatusCode = BadRequest por defecto
            Assert.AreEqual(HttpStatusCode.BadRequest, _context.Response.StatusCode);
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_NestoBusinessException_ContieneCodigoDeError()
        {
            // Arrange
            var businessException = new FacturacionException(
                "Error de facturación de prueba",
                "FACTURACION_TEST_ERROR",
                empresa: "1",
                pedido: 12345);
            _context = new HttpActionExecutedContext(_context.ActionContext, businessException);

            // Act
            _filter.OnException(_context);

            // Assert
            var content = _context.Response.Content.ReadAsAsync<Dictionary<string, object>>().Result;
            var error = content["error"] as Newtonsoft.Json.Linq.JObject;
            Assert.AreEqual("FACTURACION_TEST_ERROR", error["code"].ToString());
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_NestoBusinessException_IncluyeDetallesContexto()
        {
            // Arrange
            var businessException = new FacturacionException(
                "Error de facturación de prueba",
                "FACTURACION_TEST_ERROR",
                empresa: "1",
                pedido: 12345,
                usuario: "testuser");
            _context = new HttpActionExecutedContext(_context.ActionContext, businessException);

            // Act
            _filter.OnException(_context);

            // Assert
            var content = _context.Response.Content.ReadAsAsync<Dictionary<string, object>>().Result;
            var error = content["error"] as Newtonsoft.Json.Linq.JObject;

            Assert.IsTrue(error.ContainsKey("details"));
            var details = error["details"] as Newtonsoft.Json.Linq.JObject;
            Assert.AreEqual("1", details["empresa"].ToString());
            Assert.AreEqual("12345", details["pedido"].ToString());
            Assert.AreEqual("testuser", details["usuario"].ToString());
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_ExcepcionConInnerException_IncluyeInnerExceptionEnDebug()
        {
            // Arrange
            var innerException = new InvalidOperationException("Error interno");
            var outerException = new Exception("Error externo", innerException);
            _context = new HttpActionExecutedContext(_context.ActionContext, outerException);

            // Act
            _filter.OnException(_context);

            // Assert
            var content = _context.Response.Content.ReadAsAsync<Dictionary<string, object>>().Result;
            var error = content["error"] as Newtonsoft.Json.Linq.JObject;

            // En modo DEBUG debe incluir innerException
#if DEBUG
            Assert.IsTrue(error.ContainsKey("innerException"));
            var inner = error["innerException"] as Newtonsoft.Json.Linq.JObject;
            Assert.AreEqual("Error interno", inner["message"].ToString());
            Assert.AreEqual("InvalidOperationException", inner["type"].ToString());
#endif
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_SiempreGeneraRespuesta()
        {
            // Arrange
            var exception = new Exception("Cualquier error");
            _context = new HttpActionExecutedContext(_context.ActionContext, exception);

            // Act
            _filter.OnException(_context);

            // Assert
            Assert.IsNotNull(_context.Response);
            Assert.IsNotNull(_context.Response.Content);
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_NestoBusinessException_ConDatosAdicionales_LosIncluye()
        {
            // Arrange
            var businessException = new FacturacionException(
                "Error de facturación de prueba",
                "FACTURACION_TEST_ERROR",
                empresa: "1",
                pedido: 12345)
                .WithData("SqlErrorNumber", 547)
                .WithData("StoredProcedure", "prdCrearFacturaVta");

            _context = new HttpActionExecutedContext(_context.ActionContext, businessException);

            // Act
            _filter.OnException(_context);

            // Assert
            var content = _context.Response.Content.ReadAsAsync<Dictionary<string, object>>().Result;
            var error = content["error"] as Newtonsoft.Json.Linq.JObject;

            Assert.IsTrue(error.ContainsKey("details"));
            var details = error["details"] as Newtonsoft.Json.Linq.JObject;
            Assert.AreEqual("547", details["SqlErrorNumber"].ToString());
            Assert.AreEqual("prdCrearFacturaVta", details["StoredProcedure"].ToString());
        }
    }
}
