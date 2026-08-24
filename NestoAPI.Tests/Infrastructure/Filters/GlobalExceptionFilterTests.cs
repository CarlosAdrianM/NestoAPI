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
using System.Web.Http.Routing;

namespace NestoAPI.Tests.Infrastructure.Filters
{
    [TestClass]
    public class GlobalExceptionFilterTests
    {
        private GlobalExceptionFilter _filter;
        private HttpActionExecutedContext _context;

        // #242/#198: leer el JSON SERIALIZADO. CreateResponse guarda el Dictionary original en un
        // ObjectContent y ReadAsAsync<Dictionary<string, object>> lo devolvía SIN pasar por el
        // formatter: content["error"] era un Dictionary (no un JObject) y el "as JObject" daba
        // null. Con ReadAsStringAsync + Parse se valida lo que de verdad viaja por el cable.
        private Newtonsoft.Json.Linq.JObject LeerErrorDeLaRespuesta()
        {
            string json = _context.Response.Content.ReadAsStringAsync().Result;
            return (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(json)["error"];
        }

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

        // NestoAPI#309: sin el detalle de EntityValidationErrors, "Validation failed for one or
        // more entities" era imposible de diagnosticar a posteriori.

        [TestMethod]
        public void BuscarValidationException_LaExcepcionEsDeValidacion_LaDevuelve()
        {
            var validacion = new System.Data.Entity.Validation.DbEntityValidationException("falló");

            Assert.AreSame(validacion, GlobalExceptionFilter.BuscarValidationException(validacion));
        }

        [TestMethod]
        public void BuscarValidationException_VieneEnvueltaComoInner_LaEncuentra()
        {
            var validacion = new System.Data.Entity.Validation.DbEntityValidationException("falló");
            var envoltorio = new Exception("No se ha podido actualizar el cliente",
                new Exception("capa intermedia", validacion));

            Assert.AreSame(validacion, GlobalExceptionFilter.BuscarValidationException(envoltorio));
        }

        [TestMethod]
        public void BuscarValidationException_SinValidacionEnLaCadena_DevuelveNull()
        {
            var generica = new Exception("otra cosa", new InvalidOperationException());

            Assert.IsNull(GlobalExceptionFilter.BuscarValidationException(generica));
        }

        [TestMethod]
        public void GlobalExceptionFilter_OnException_DbEntityValidation_Devuelve400ConCodigoEspecifico()
        {
            var validacion = new System.Data.Entity.Validation.DbEntityValidationException("falló");
            _context = new HttpActionExecutedContext(_context.ActionContext, validacion);

            _filter.OnException(_context);

            Assert.AreEqual(HttpStatusCode.BadRequest, _context.Response.StatusCode);
            var error = LeerErrorDeLaRespuesta();
            Assert.AreEqual("ENTITY_VALIDATION_ERROR", error["code"].ToString());
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
            var error = LeerErrorDeLaRespuesta();
            Assert.IsNotNull(error, "La respuesta debe traer la clave 'error'");
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
            var error = LeerErrorDeLaRespuesta();
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
            var error = LeerErrorDeLaRespuesta();
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
            var error = LeerErrorDeLaRespuesta();

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
            var error = LeerErrorDeLaRespuesta();

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
            var error = LeerErrorDeLaRespuesta();

            Assert.IsTrue(error.ContainsKey("details"));
            var details = error["details"] as Newtonsoft.Json.Linq.JObject;
            Assert.AreEqual("547", details["SqlErrorNumber"].ToString());
            Assert.AreEqual("prdCrearFacturaVta", details["StoredProcedure"].ToString());
        }

        // ===== NestoAPI#361: las denegaciones de negocio no ensucian ELMAH =====
        // Hasta el 24/08/26 GlobalExceptionFilter registraba TODO en ELMAH, tambien las
        // excepciones de negocio. En una semana, 25 de 237 entradas eran denegaciones esperadas
        // (validaciones de pedido y "no hay stock para asignar picking"). Regla nueva:
        // 4xx = negocio (no se registra), 5xx = fallo (se registra), y el que lanza puede
        // forzarlo con RegistrarEnLog.

        [TestMethod]
        public void DebeRegistrarseEnElmah_ExcepcionDeNegocioCon400_NoSeRegistra()
        {
            var negocio = new NestoBusinessException("No hay stock suficiente para asignar picking");

            Assert.AreEqual(HttpStatusCode.BadRequest, negocio.StatusCode, "El default de negocio es 400");
            Assert.IsFalse(GlobalExceptionFilter.DebeRegistrarseEnElmah(negocio));
        }

        [TestMethod]
        public void DebeRegistrarseEnElmah_ExcepcionDeNegocioCon500_SiSeRegistra()
        {
            // Un 5xx en una excepcion de negocio significa que algo se rompio de verdad.
            var negocio = new NestoBusinessException("Fallo al contabilizar")
            {
                StatusCode = HttpStatusCode.InternalServerError
            };

            Assert.IsTrue(GlobalExceptionFilter.DebeRegistrarseEnElmah(negocio));
        }

        [TestMethod]
        public void DebeRegistrarseEnElmah_ExcepcionDeNegocioQuePideRegistrarse_SiSeRegistra()
        {
            // Valvula de escape para los casos que ademas de denegar interesa vigilar.
            var negocio = new NestoBusinessException("Denegado, pero quiero verlo")
            {
                RegistrarEnLog = true
            };

            Assert.IsTrue(GlobalExceptionFilter.DebeRegistrarseEnElmah(negocio));
        }

        [TestMethod]
        public void DebeRegistrarseEnElmah_ExcepcionQueNoEsDeNegocio_SiSeRegistra()
        {
            // Lo que NO es de negocio sigue yendo a ELMAH aunque su mensaje suene a aviso:
            // los RAISERROR de los SP y las excepciones de los jobs se revisan una a una.
            Assert.IsTrue(GlobalExceptionFilter.DebeRegistrarseEnElmah(
                new InvalidOperationException("La secuencia contiene mas de un elemento")));
            Assert.IsTrue(GlobalExceptionFilter.DebeRegistrarseEnElmah(
                new Exception("[WARNING] No se puede facturar porque tiene marcado el servir junto")));
        }

        [TestMethod]
        public void DebeRegistrarseEnElmah_ValidacionDePedido_SI_SeRegistra()
        {
            // EXCEPCION DELIBERADA a la regla "el negocio no va a ELMAH": aunque devuelve 400,
            // PedidoValidacionException adjunta el pedido serializado (#215) y esa ficha se usa
            // para REPRODUCIR la validacion denegada. Carlos la usa, asi que se conserva.
            var validacion = new PedidoValidacionException(
                "No se encuentra autorizado el descuento del 100,00 % para el producto 45001",
                null, empresa: "1", pedido: 924645);

            Assert.AreEqual(HttpStatusCode.BadRequest, validacion.StatusCode, "sigue siendo un 400 para el cliente");
            Assert.IsTrue(validacion.RegistrarEnLog);
            Assert.IsTrue(GlobalExceptionFilter.DebeRegistrarseEnElmah(validacion),
                "si esto se pone en falso, se pierde la capacidad de reproducir pedidos denegados (#215)");
        }

        [TestMethod]
        public void OnException_ExcepcionDeNegocio_SigueDevolviendoEl400ConSuMensaje()
        {
            // Que no se registre en ELMAH no cambia NADA de la respuesta al cliente.
            _context.Exception = new NestoBusinessException("No hay stock suficiente para asignar picking");

            _filter.OnException(_context);

            Assert.AreEqual(HttpStatusCode.BadRequest, _context.Response.StatusCode);
            var error = LeerErrorDeLaRespuesta();
            Assert.AreEqual("No hay stock suficiente para asignar picking", error["message"].ToString());
        }
    }
}
