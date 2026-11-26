using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Caso del 20/11/26: POST api/RepartoPropio/Cobros dos veces en un minuto al cliente 9251 y el segundo cobro quedó entero a
    /// cuenta. Decisión de Carlos (26/11/26): frenar y pedir confirmación (409 COBRO_REPETIDO / COBRO_TODO_A_CUENTA salvo Confirmar).
    /// </summary>
    [TestClass]
    public class CobroRepetidoRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 20, 12, 32, 0);
        private const string MIGUEL = "NUEVAVISION\\Miguel";
        private const string CLIENTE = "9251";

        private IRepositorioCobrosReparto repositorio;
        private ILectorCobrosRecientesReparto recientes;
        private ServicioCobrosReparto servicio;
        private List<CobroRecienteReparto> cobrosBd;
        private List<EfectoCobroReparto> efectosBd;

        [TestInitialize]
        public void Setup()
        {
            cobrosBd = new List<CobroRecienteReparto>();
            efectosBd = new List<EfectoCobroReparto>();
            repositorio = A.Fake<IRepositorioCobrosReparto>();
            A.CallTo(() => repositorio.LeerPorClave(A<Guid>._)).Returns(Task.FromResult<ResultadoCobroDeudaDTO>(null));
            A.CallTo(() => repositorio.LeerEfectos(A<IReadOnlyCollection<int>>._))
                .ReturnsLazily((IReadOnlyCollection<int> ids) => Task.FromResult(efectosBd.Where(e => ids.Contains(e.Id)).ToList()));
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._))
                .Returns(Task.FromResult(777));
            recientes = A.Fake<ILectorCobrosRecientesReparto>();
            A.CallTo(() => recientes.LeerCobrosDelCliente(A<string>._, A<DateTime>._))
                .ReturnsLazily(() => Task.FromResult(cobrosBd.ToList()));
            ILectorParametrosUsuario parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", "Miguel", "CajaDefecto")).Returns("57000032");
            A.CallTo(() => parametros.LeerParametro("1", "Miguel", "DiarioCaja")).Returns("_Caja");
            servicio = new ServicioCobrosReparto(repositorio, parametros, () => AHORA) { CobrosRecientes = recientes };
        }

        private void Efecto(int id, decimal pendiente)
            => efectosBd.Add(new EfectoCobroReparto
            {
                Id = id, Empresa = "1", Cliente = CLIENTE, Contacto = "0", TipoApunte = "2", Documento = "NV26001", Efecto = "1",
                Importe = pendiente, ImportePendiente = pendiente, Vencimiento = new DateTime(2026, 11, 1), Estado = "NRM"
            });

        private static CobroDeudaRepartoDTO Peticion(decimal importe, params int[] efectos)
            => new CobroDeudaRepartoDTO { Cliente = CLIENTE, Efectos = efectos.ToList(), Importe = importe, ClaveIdempotencia = Guid.NewGuid() };

        private void CobroAnterior(Guid clave, DateTime fecha, decimal importe = 84.80m, string usuario = MIGUEL, string cliente = CLIENTE)
            => cobrosBd.Add(new CobroRecienteReparto { Clave = clave, Cliente = cliente, Importe = importe, Fecha = fecha, Usuario = usuario });

        private void NoSeRegistra()
            => A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._)).MustNotHaveHappened();

        [TestMethod]
        public async Task Cobrar_OtroCobroDelClienteHaceUnMinuto_409CobroRepetidoYNoContabiliza()
        {
            Efecto(1, 84.80m);
            CobroAnterior(Guid.NewGuid(), new DateTime(2026, 11, 20, 12, 31, 0));

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(84.80m, 1), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual(ReglaCobroRepetidoReparto.CODIGO_REPETIDO, r.Codigo);
            Assert.AreEqual("Ya ha cobrado 84,80 € a este cliente a las 12:31. Si es otro cobro distinto, confírmelo.", r.Mensaje);
            NoSeRegistra();
        }

        [TestMethod]
        public async Task Cobrar_RepetidoConConfirmar_Contabiliza()
        {
            Efecto(1, 84.80m);
            CobroAnterior(Guid.NewGuid(), new DateTime(2026, 11, 20, 12, 31, 0));
            CobroDeudaRepartoDTO peticion = Peticion(84.80m, 1);
            peticion.Confirmar = true;

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(peticion, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(777, r.Contenido.Asiento);
        }

        [TestMethod]
        public async Task Cobrar_ReintentoConLaMismaClave_DevuelveLoDeAntesSinFrenar()
        {
            var anterior = new ResultadoCobroDeudaDTO { Asiento = 555, Importe = 84.80m, ImporteACuenta = 84.80m };
            CobroDeudaRepartoDTO peticion = Peticion(84.80m);
            A.CallTo(() => repositorio.LeerPorClave(peticion.ClaveIdempotencia)).Returns(Task.FromResult(anterior));
            CobroAnterior(peticion.ClaveIdempotencia, new DateTime(2026, 11, 20, 12, 31, 0));

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(peticion, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo);
            Assert.AreSame(anterior, r.Contenido);
            NoSeRegistra();
        }

        [TestMethod]
        public async Task Cobrar_CobroAnteriorDeHaceMasDeTreintaMinutos_NoFrena()
        {
            Efecto(1, 50m);
            CobroAnterior(Guid.NewGuid(), AHORA.AddMinutes(-31));

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(50m, 1), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
        }

        [TestMethod]
        public async Task Cobrar_CobroRecienteDeOtroCliente_NoFrena()
        {
            Efecto(1, 50m);
            CobroAnterior(Guid.NewGuid(), AHORA.AddMinutes(-1), cliente: "15191");

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(50m, 1), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
        }

        [TestMethod]
        public async Task Cobrar_RepetidoDeOtroRepartidor_ElMensajeDiceQuien()
        {
            Efecto(1, 50m);
            CobroAnterior(Guid.NewGuid(), new DateTime(2026, 11, 20, 12, 5, 0), 20m, "NUEVAVISION\\Andre");

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(50m, 1), MIGUEL);

            Assert.AreEqual("Andre ya ha cobrado 20,00 € a este cliente a las 12:05. Si es otro cobro distinto, confírmelo.", r.Mensaje);
        }

        [TestMethod]
        public async Task Cobrar_SinEfectos_409TodoACuenta()
        {
            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(30m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual(ReglaCobroRepetidoReparto.CODIGO_TODO_A_CUENTA, r.Codigo);
            Assert.AreEqual("Este cobro no corresponde a ninguna deuda del cliente: quedaría entero a cuenta. Confírmelo si es correcto.", r.Mensaje);
            NoSeRegistra();
        }

        // El caso real: el segundo cobro marcaba el mismo efecto, que ya no estaba pendiente
        [TestMethod]
        public async Task Cobrar_EfectoQueYaNoEstaPendiente_409TodoACuenta()
        {
            efectosBd.Add(new EfectoCobroReparto { Id = 1, Empresa = "1", Cliente = CLIENTE, ImportePendiente = 0m, Estado = "NRM" });

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(84.80m, 1), MIGUEL);

            Assert.AreEqual(ReglaCobroRepetidoReparto.CODIGO_TODO_A_CUENTA, r.Codigo);
            NoSeRegistra();
        }

        [TestMethod]
        public async Task Cobrar_TodoACuentaConConfirmar_Contabiliza()
        {
            CobroDeudaRepartoDTO peticion = Peticion(30m);
            peticion.Confirmar = true;

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(peticion, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(30m, r.Contenido.ImporteACuenta);
        }

        [TestMethod]
        public async Task Cobrar_ParteACuentaPeroLiquidaUnEfecto_NoFrena()
        {
            Efecto(1, 50m);

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(80m, 1), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(30m, r.Contenido.ImporteACuenta);
        }

        // La nota de cobro (dentro de la entrega) usa el servicio sin el control: no se frena nunca
        [TestMethod]
        public async Task Cobrar_SinControl_ComoAntes()
        {
            servicio.CobrosRecientes = null;
            CobroAnterior(Guid.NewGuid(), AHORA.AddMinutes(-1));

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(30m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            A.CallTo(() => recientes.LeerCobrosDelCliente(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_CobroRepetido_409ConCodigoArribaYEnError()
        {
            Efecto(1, 84.80m);
            CobroAnterior(Guid.NewGuid(), new DateTime(2026, 11, 20, 12, 31, 0));
            var almacen = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, MIGUEL), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN)
            }, "prueba"));
            var controlador = new RepartoPropioController(null, null)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = almacen,
                ServicioCobros = servicio
            };

            HttpResponseMessage respuesta = await (await controlador.Cobrar(Peticion(84.80m, 1))).ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            JObject cuerpo = JObject.Parse(await respuesta.Content.ReadAsStringAsync());
            Assert.AreEqual("COBRO_REPETIDO", (string)cuerpo["Codigo"]);
            Assert.AreEqual("COBRO_REPETIDO", (string)cuerpo["error"]["code"]);
            StringAssert.StartsWith((string)cuerpo["Message"], "Ya ha cobrado 84,80 €");
        }

        [TestMethod]
        public void Regla_CobroQueFrena_ElMasRecienteDelCliente()
        {
            Guid clave = Guid.NewGuid();
            var cobros = new List<CobroRecienteReparto>
            {
                new CobroRecienteReparto { Clave = Guid.NewGuid(), Cliente = "9251  ", Importe = 10m, Fecha = AHORA.AddMinutes(-20) },
                new CobroRecienteReparto { Clave = Guid.NewGuid(), Cliente = "9251", Importe = 20m, Fecha = AHORA.AddMinutes(-5) },
                new CobroRecienteReparto { Clave = clave, Cliente = "9251", Importe = 30m, Fecha = AHORA.AddMinutes(-1) }
            };

            CobroRecienteReparto frena = ReglaCobroRepetidoReparto.CobroQueFrena(cobros, " 9251 ", clave, AHORA);

            Assert.AreEqual(20m, frena.Importe);
        }
    }
}
