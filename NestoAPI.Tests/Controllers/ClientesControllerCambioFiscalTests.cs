using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Clientes.CambioFiscal;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#876: los endpoints de api/Clientes/CambioFiscal y el campo del alta.</summary>
    [TestClass]
    public class ClientesControllerCambioFiscalTests
    {
        private static ClientesController Controlador(IRepositorioCambioFiscal repositorio, params Claim[] claims)
            => Controlador(repositorio, true, claims);

        private static ClientesController Controlador(IRepositorioCambioFiscal repositorio, bool hayTabla, params Claim[] claims)
        {
            IRepositorioClientesAnteriores anteriores = A.Fake<IRepositorioClientesAnteriores>();
            A.CallTo(() => anteriores.TablaExiste()).Returns(hayTabla);
            List<Claim> todas = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Pepe") };
            todas.AddRange(claims);
            return new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>())
            {
                RepositorioCambioFiscal = repositorio,
                RepositorioClientesAnteriores = anteriores,
                User = new ClaimsPrincipal(new ClaimsIdentity(todas, "prueba"))
            };
        }

        [TestMethod]
        public async Task Copiar_ClienteDeLaTienda_403()
        {
            IRepositorioCambioFiscal repo = A.Fake<IRepositorioCambioFiscal>();
            ClientesController controller = Controlador(repo, new Claim("cliente", "42178"));

            IHttpActionResult r = await controller.PostCopiarCambioFiscal(new CopiaCambioFiscalRequest { ClienteAnterior = "37283", ClienteNuevo = "42178" });

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)r).StatusCode);
            A.CallTo(() => repo.Leer(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Copiar_SinTabla_503()
        {
            IRepositorioCambioFiscal repo = A.Fake<IRepositorioCambioFiscal>();

            IHttpActionResult r = await Controlador(repo, false).PostCopiarCambioFiscal(new CopiaCambioFiscalRequest { ClienteAnterior = "37283", ClienteNuevo = "42178" });

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, EstadoDe(r));
        }

        [TestMethod]
        public async Task Copiar_OficinaYNoEsCambioFiscal_409ConCodigo()
        {
            IRepositorioCambioFiscal repo = A.Fake<IRepositorioCambioFiscal>();
            A.CallTo(() => repo.Leer(A<string>._, A<string>._, A<string>._)).Returns(new DatosCambioFiscal
            {
                Anterior = new FotoClienteCambioFiscal { Cliente = "37283", Fichas = new List<FichaCambioFiscal> { new FichaCambioFiscal { Contacto = "0", ClientePrincipal = true } } },
                Nuevo = new FotoClienteCambioFiscal { Cliente = "42178", Fichas = new List<FichaCambioFiscal> { new FichaCambioFiscal { Contacto = "0", ClientePrincipal = true } } },
                OrigenNuevo = "WEB"
            });

            IHttpActionResult r = await Controlador(repo, new Claim(ClaimTypes.Role, "NUEVAVISION\\Administración"))
                .PostCopiarCambioFiscal(new CopiaCambioFiscalRequest { ClienteAnterior = "37283", ClienteNuevo = "42178" }, ensayo: false);

            Assert.AreEqual(HttpStatusCode.Conflict, EstadoDe(r));
            A.CallTo(() => repo.Copiar(A<string>._, A<string>._, A<string>._, A<Func<DatosCambioFiscal, DecisionCopiaCambioFiscal>>._, A<string>._)).MustNotHaveHappened();
        }

        // El alta: las validaciones de los cortes 1-2 (ReglaClienteAnterior, ver ClientesAnterioresTests) y, encima, las de CopiarDelAnterior

        [TestMethod]
        public async Task Alta_ClienteAnteriorSinOrigenCambioFiscal_400()
        {
            string motivo = (await Controlador(A.Fake<IRepositorioCambioFiscal>())
                .ValidarClienteAnteriorAlta(new ClienteCrear { ClienteAnterior = "37283", Origen = "WEB" })).Error;

            Assert.AreEqual(ReglaClienteAnterior.MOTIVO_SIN_CAMBIO_FISCAL, motivo);
        }

        [TestMethod]
        public async Task Alta_CopiarSinClienteAnterior_400()
        {
            string motivo = (await Controlador(A.Fake<IRepositorioCambioFiscal>())
                .ValidarClienteAnteriorAlta(new ClienteCrear { CopiarDelAnterior = new List<string>() })).Error;

            StringAssert.Contains(motivo, "ClienteAnterior");
        }

        [TestMethod]
        public async Task Alta_CambioFiscalSinClienteAnterior_400Obligatorio()
        {
            // Decisión de Carlos (26/11/26): Nesto ya lo manda, así que con CAMBIO-FISCAL es obligatorio (antes, en el corte nuevo, era opcional)
            string motivo = (await Controlador(A.Fake<IRepositorioCambioFiscal>())
                .ValidarClienteAnteriorAlta(new ClienteCrear { Origen = "CAMBIO-FISCAL" })).Error;

            Assert.AreEqual(ReglaClienteAnterior.MOTIVO_FALTA, motivo);
        }

        [TestMethod]
        public async Task Alta_SinOrigenNiClienteAnterior_ComoSiempre()
        {
            ValidacionClienteAnterior v = await Controlador(A.Fake<IRepositorioCambioFiscal>()).ValidarClienteAnteriorAlta(new ClienteCrear { Origen = "WEB" });

            Assert.IsNull(v.Error);
            Assert.IsNull(v.Anterior);
        }

        [TestMethod]
        public async Task Alta_BloqueQueNoExiste_400()
        {
            string motivo = (await Controlador(A.Fake<IRepositorioCambioFiscal>())
                .ValidarClienteAnteriorAlta(new ClienteCrear { ClienteAnterior = "37283", Origen = "cambio-fiscal", CopiarDelAnterior = new List<string> { "Nif" } })).Error;

            StringAssert.Contains(motivo, "Nif");
        }

        // Carlos 27/11/26: los tres llevan [Authorize] (su único llamante, Nesto, va con token por ConfigurarAutorizacion).
        private static bool TieneAuthorize(string metodo)
            => typeof(ClientesController).GetMethod(metodo).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0;

        [TestMethod]
        public void Previa_LlevaAuthorize()
        {
            Assert.IsTrue(TieneAuthorize(nameof(ClientesController.GetPreviaCambioFiscal)));
        }

        [TestMethod]
        public void Copiar_LlevaAuthorize()
        {
            Assert.IsTrue(TieneAuthorize(nameof(ClientesController.PostCopiarCambioFiscal)));
        }

        [TestMethod]
        public void PasarPedidos_LlevaAuthorize()
        {
            Assert.IsTrue(TieneAuthorize(nameof(ClientesController.PostPasarPedidosCambioFiscal)));
        }

        private static HttpStatusCode EstadoDe(IHttpActionResult r)
        {
            object estado = r.GetType().GetProperty("StatusCode")?.GetValue(r);
            return estado is HttpStatusCode h ? h : HttpStatusCode.OK;
        }
    }
}
