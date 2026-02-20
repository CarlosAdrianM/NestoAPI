using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    [TestClass]
    public class ServicioReclamacionDeudaTests
    {
        private IRedsysService _redsysService;
        private ServicioReclamacionDeuda _servicio;

        [TestInitialize]
        public void Setup()
        {
            _redsysService = A.Fake<IRedsysService>();
            _servicio = new ServicioReclamacionDeuda(_redsysService);
        }

        [TestMethod]
        public async Task ProcesarReclamacionDeuda_LlamaCrearParametrosP2F_ConDatosCorrectos()
        {
            // Arrange
            var reclamacion = new ReclamacionDeuda
            {
                Importe = 150.50m,
                Correo = "test@test.com",
                Movil = "600123456",
                TextoSMS = "Pague su deuda",
                Cliente = "15191",
                Nombre = "Juan",
                Direccion = "juan@test.com",
                Asunto = "Deuda pendiente"
            };

            A.CallTo(() => _redsysService.CrearParametrosP2F(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._,
                A<FormatoCorreoReclamacion>._))
                .Returns(new ParametrosRedsysFirmados
                {
                    Ds_MerchantParameters = "params",
                    Ds_Signature = "firma",
                    Ds_SignatureVersion = "HMAC_SHA256_V1"
                });

            A.CallTo(() => _redsysService.EnviarPeticionREST(A<ParametrosRedsysFirmados>._))
                .Returns(Task.FromResult(new RespuestaRedsys
                {
                    Ds_UrlPago2Fases = "https://redsys.es/pago/123"
                }));

            // Act
            await _servicio.ProcesarReclamacionDeuda(reclamacion);

            // Assert
            A.CallTo(() => _redsysService.CrearParametrosP2F(
                150.50m, "test@test.com", "600123456", "Pague su deuda", "15191",
                A<FormatoCorreoReclamacion>.That.Matches(f =>
                    f.nombreComprador == "Juan" &&
                    f.direccionComprador == "juan@test.com" &&
                    f.subjectMailCliente == "Deuda pendiente")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarReclamacionDeuda_ConRespuestaExitosa_DevuelveEnlaceYTramitadoOK()
        {
            // Arrange
            var reclamacion = new ReclamacionDeuda
            {
                Importe = 100m,
                Correo = "test@test.com",
                Cliente = "10000"
            };

            A.CallTo(() => _redsysService.CrearParametrosP2F(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._,
                A<FormatoCorreoReclamacion>._))
                .Returns(new ParametrosRedsysFirmados());

            A.CallTo(() => _redsysService.EnviarPeticionREST(A<ParametrosRedsysFirmados>._))
                .Returns(Task.FromResult(new RespuestaRedsys
                {
                    Ds_UrlPago2Fases = "https://redsys.es/pago/456"
                }));

            // Act
            ReclamacionDeuda resultado = await _servicio.ProcesarReclamacionDeuda(reclamacion);

            // Assert
            Assert.IsTrue(resultado.TramitadoOK);
            Assert.AreEqual("https://redsys.es/pago/456", resultado.Enlace);
        }

        [TestMethod]
        public async Task ProcesarReclamacionDeuda_ConDatosCorreo_RellenaNombreAsuntoDireccion()
        {
            // Arrange
            var reclamacion = new ReclamacionDeuda
            {
                Importe = 50m,
                Correo = "correo@ejemplo.com",
                Cliente = "12345",
                Nombre = "María López",
                Direccion = "maria@ejemplo.com",
                Asunto = "Reclamación deuda"
            };

            FormatoCorreoReclamacion datosCapturados = null;
            A.CallTo(() => _redsysService.CrearParametrosP2F(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._,
                A<FormatoCorreoReclamacion>._))
                .Invokes((decimal i, string c, string m, string t, string cl, FormatoCorreoReclamacion f) =>
                {
                    datosCapturados = f;
                })
                .Returns(new ParametrosRedsysFirmados());

            A.CallTo(() => _redsysService.EnviarPeticionREST(A<ParametrosRedsysFirmados>._))
                .Returns(Task.FromResult(new RespuestaRedsys()));

            // Act
            await _servicio.ProcesarReclamacionDeuda(reclamacion);

            // Assert
            Assert.IsNotNull(datosCapturados);
            Assert.AreEqual("María López", datosCapturados.nombreComprador);
            Assert.AreEqual("maria@ejemplo.com", datosCapturados.direccionComprador);
            Assert.AreEqual("Reclamación deuda", datosCapturados.subjectMailCliente);
            Assert.IsTrue(datosCapturados.textoLibre1.Contains("NUEVA VISION"));
        }

        [TestMethod]
        public async Task ProcesarReclamacionDeuda_ConRespuestaExitosa_LlamaEnviarPeticionREST()
        {
            // Arrange
            var reclamacion = new ReclamacionDeuda
            {
                Importe = 200m,
                Correo = "admin@test.com",
                Cliente = "20000",
                Nombre = "Test"
            };

            A.CallTo(() => _redsysService.CrearParametrosP2F(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._,
                A<FormatoCorreoReclamacion>._))
                .Returns(new ParametrosRedsysFirmados
                {
                    NumeroOrden = "TEST12345678",
                    Ds_MerchantParameters = "params",
                    Ds_Signature = "firma"
                });

            A.CallTo(() => _redsysService.EnviarPeticionREST(A<ParametrosRedsysFirmados>._))
                .Returns(Task.FromResult(new RespuestaRedsys
                {
                    Ds_UrlPago2Fases = "https://redsys.es/pago/789"
                }));

            // Act
            ReclamacionDeuda resultado = await _servicio.ProcesarReclamacionDeuda(reclamacion, "DOMINIO\\usuario");

            // Assert — verificar que se llamó a EnviarPeticionREST con los parámetros generados
            A.CallTo(() => _redsysService.EnviarPeticionREST(
                A<ParametrosRedsysFirmados>.That.Matches(p => p.NumeroOrden == "TEST12345678")))
                .MustHaveHappenedOnceExactly();
            Assert.IsTrue(resultado.TramitadoOK);
        }
    }
}
