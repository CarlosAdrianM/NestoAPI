using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Picking;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#799 (Alfredo 03/11/26, pedido 928723, contacto 3 del cliente 2077): «es una excepción de ruta de miércoles, aunque
    /// su zona sea de martes». La excepción del contacto (ClientesDiaReparto) manda sobre el código postal (CodigosPostalesDiaReparto)
    /// en todo lo que mira el día de reparto de nuestra ruta.
    /// <para>Semana de referencia: lunes 02/11/2026 … viernes 06/11/2026. CP 28007 martes, 28001 miércoles; 28760 sin día.</para>
    /// </summary>
    [TestClass]
    public class DiasRepartoClientesTests
    {
        private const string CP_MARTES = "28007";
        private const string CP_MIERCOLES = "28001";
        private const string CP_SIN_DIA = "28760";
        private static readonly TimeSpan CORTE_AGENCIA = new TimeSpan(11, 0, 0);

        private static DateTime D(string valor) => DateTime.ParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static DateTime Instante(string valor) => DateTime.ParseExact(valor, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        private static bool FinDeSemana(DateTime f) => f.DayOfWeek == DayOfWeek.Saturday || f.DayOfWeek == DayOfWeek.Sunday;

        private static ExcepcionDiaRepartoCliente Excepcion(string cliente, string contacto, string ruta, string empresa = "1") =>
            new ExcepcionDiaRepartoCliente { Empresa = empresa, Cliente = cliente, Contacto = contacto, Ruta = ruta, Motivo = "Alfredo" };

        [TestInitialize]
        public void Preparar()
        {
            RutasPropiasConfiguradas.Reiniciar(); // AT y 16
            DiasRepartoCodigosPostales.ConfigurarPara(new Dictionary<string, string> { [CP_MARTES] = "02", [CP_MIERCOLES] = "03" });
            DiasRepartoClientes.ConfigurarPara(Excepcion("2077", "3", "03"));
        }

        [TestCleanup]
        public void Limpiar()
        {
            RutasPropiasConfiguradas.Reiniciar();
            DiasRepartoCodigosPostales.Reiniciar();
            DiasRepartoClientes.Reiniciar();
        }

        // ================================================================ DiasRepartoClientes

        [TestMethod]
        public void DiaDe_ConExcepcion_SuDia()
        {
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoClientes.DiaDe("1", "2077", "3"));
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoClientes.DiaDe("1  ", "2077      ", "3  "), "Con el relleno de los char de la BD");
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoClientes.DiaDe(null, "2077", "3"), "Sin empresa, la por defecto");
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoClientes.DiaDe("3", "2077", "3"), "Otra empresa sin fila propia mira la 1");
        }

        [TestMethod]
        public void DiaDe_SinExcepcion_Null()
        {
            Assert.IsNull(DiasRepartoClientes.DiaDe("1", "2077", "0"), "Otro contacto del mismo cliente");
            Assert.IsNull(DiasRepartoClientes.DiaDe("1", "15191", "3"));
            Assert.IsNull(DiasRepartoClientes.DiaDe("1", null, "3"));
            Assert.IsNull(DiasRepartoClientes.DiaDe("1", "2077", null));
        }

        [TestMethod]
        public void DiaDe_SinTabla_NadaYNadaFalla()
        {
            // El lector de la BD con la tabla sin crear devuelve cero filas (IF OBJECT_ID … IS NOT NULL)
            DiasRepartoClientes.Configurar(() => new List<ExcepcionDiaRepartoCliente>());
            Assert.IsNull(DiasRepartoClientes.DiaDe("1", "2077", "3"));
            Assert.IsFalse(DiasRepartoClientes.HayAlguna);

            var errores = new List<Exception>();
            DiasRepartoClientes.Configurar(() => throw new InvalidOperationException("sin conexión"), errores.Add);
            Assert.IsNull(DiasRepartoClientes.DiaDe("1", "2077", "3"));
            Assert.AreEqual(1, errores.Count);

            DiasRepartoClientes.Reiniciar();
            Assert.IsNull(DiasRepartoClientes.DiaDe("1", "2077", "3"), "Sin lector (arranque sin BD)");
        }

        [TestMethod]
        public void Interpretar_IgnoraRutasQueNoSonDeLunesAViernesYFilasSinContacto()
        {
            IReadOnlyDictionary<string, DayOfWeek> dias = DiasRepartoClientes.Interpretar(new[]
            {
                Excepcion("1", "0", "16"),
                Excepcion("2", "0", "00"),
                Excepcion("3", null, "02"),
                Excepcion("4", "0", "05 ")
            });
            Assert.AreEqual(1, dias.Count);
            Assert.AreEqual(DayOfWeek.Friday, dias[DiasRepartoClientes.Clave("1", "4", "0")]);
        }

        [TestMethod]
        public void Cache_DiezMinutos_EInvalidarLaVuelveALeer()
        {
            int lecturas = 0;
            string ruta = "03";
            DateTime ahora = new DateTime(2026, 11, 3, 10, 0, 0);
            DiasRepartoClientes.Configurar(() => { lecturas++; return new[] { Excepcion("2077", "3", ruta) }; }, null, () => ahora);

            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoClientes.DiaDe("1", "2077", "3"));
            ruta = "04";
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoClientes.DiaDe("1", "2077", "3"), "En caché");
            Assert.AreEqual(1, lecturas);

            DiasRepartoClientes.Invalidar();
            Assert.AreEqual(DayOfWeek.Thursday, DiasRepartoClientes.DiaDe("1", "2077", "3"), "El PUT invalida: al momento");
            Assert.AreEqual(2, lecturas);

            ruta = "05";
            ahora = ahora + DiasRepartoClientes.DURACION_CACHE;
            Assert.AreEqual(DayOfWeek.Friday, DiasRepartoClientes.DiaDe("1", "2077", "3"), "Al caducar");
        }

        // ================================================================ La regla: DiaRutaInterna con el cliente

        [TestMethod]
        public void DiaRutaInterna_LaExcepcionMandaSobreElCodigoPostal()
        {
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoCodigosPostales.DiaRutaInterna("AT", CP_MARTES, "1", "2077", "3"));
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoCodigosPostales.DiaRutaInterna("16", CP_SIN_DIA, "1", "2077", "3"),
                "Con excepción, aunque su código postal no tenga día");
        }

        [TestMethod]
        public void DiaRutaInterna_SinExcepcion_ElDeSuCodigoPostal()
        {
            Assert.AreEqual(DayOfWeek.Tuesday, DiasRepartoCodigosPostales.DiaRutaInterna("AT", CP_MARTES, "1", "2077", "0"));
            Assert.AreEqual(DayOfWeek.Tuesday, DiasRepartoCodigosPostales.DiaRutaInterna("AT", CP_MARTES, null, null, null));
            Assert.AreEqual(DayOfWeek.Tuesday, DiasRepartoCodigosPostales.DiaRutaInterna("AT", CP_MARTES), "La de siempre, sin cliente");
            Assert.IsNull(DiasRepartoCodigosPostales.DiaRutaInterna("AT", CP_SIN_DIA, "1", "2077", "0"));
        }

        [TestMethod]
        public void DiaRutaInterna_RutaQueNoEsPropia_Null()
        {
            Assert.IsNull(DiasRepartoCodigosPostales.DiaRutaInterna("FW", CP_MARTES, "1", "2077", "3"));
            Assert.IsNull(DiasRepartoCodigosPostales.DiaRutaInterna(null, CP_MARTES, "1", "2077", "3"));
        }

        [TestMethod]
        public void PuedeTenerDia_ConSoloExcepciones_SiEnRutaPropia()
        {
            DiasRepartoCodigosPostales.Reiniciar();
            Assert.IsTrue(DiasRepartoCodigosPostales.PuedeTenerDia("AT"));
            Assert.IsFalse(DiasRepartoCodigosPostales.PuedeTenerDia("FW"));
            DiasRepartoClientes.Reiniciar();
            Assert.IsFalse(DiasRepartoCodigosPostales.PuedeTenerDia("AT"));
        }

        // ================================================================ Los sitios que lo usan

        [TestMethod]
        public void FechaEntregaAjustada_ConExcepcionDelCliente_ElMiercolesAunqueSuCodigoPostalSeaDeMartes()
        {
            // Lunes 02/11 a las 10:00: por el CP saldría el martes 03; con la excepción, el miércoles 04
            DayOfWeek? conCliente = DiasRepartoCodigosPostales.DiaRutaInterna("AT", CP_MARTES, "1", "2077", "3");
            DayOfWeek? sinCliente = DiasRepartoCodigosPostales.DiaRutaInterna("AT", CP_MARTES, "1", "2077", "0");
            Func<DateTime, string, bool> festivos = (f, a) => FinDeSemana(f);

            Assert.AreEqual(D("2026-11-04"), GestorPedidosVenta.FechaEntregaAjustada(D("2026-11-02"), "AT", Constantes.Almacenes.ALGETE,
                Instante("2026-11-02 10:00"), CORTE_AGENCIA, festivos, conCliente));
            Assert.AreEqual(D("2026-11-03"), GestorPedidosVenta.FechaEntregaAjustada(D("2026-11-02"), "AT", Constantes.Almacenes.ALGETE,
                Instante("2026-11-02 10:00"), CORTE_AGENCIA, festivos, sinCliente));
        }

        [TestMethod]
        public void FechasFuturas_Clave_ConExcepcionNoSeMezclaConElCodigoPostal()
        {
            Assert.AreEqual("AT|28007", FechasFuturasPedido.Clave("AT", CP_MARTES));
            Assert.AreEqual("AT|28007", FechasFuturasPedido.Clave("AT", CP_MARTES, "1", "2077", "0"));
            Assert.AreEqual("AT|@" + (int)DayOfWeek.Wednesday, FechasFuturasPedido.Clave("AT", CP_MARTES, "1", "2077", "3"));
            Assert.AreEqual("FW", FechasFuturasPedido.Clave("FW", CP_MARTES, "1", "2077", "3"));
        }

        [TestMethod]
        public void Picking_DiaReparto_ConElContactoDelPedido()
        {
            var pedido = new PedidoPicking { Empresa = "1", Cliente = "2077", Contacto = "3", Ruta = "AT", CodigoPostal = CP_MARTES };
            Assert.AreEqual(DayOfWeek.Wednesday, GestorRutaInternaPicking.DiaReparto(pedido));
            pedido.Contacto = "0";
            Assert.AreEqual(DayOfWeek.Tuesday, GestorRutaInternaPicking.DiaReparto(pedido));
        }

        [TestMethod]
        public void AvisoCierre_MiraElDiaDeLaExcepcion()
        {
            // Cierra los miércoles ("11011"): con la excepción de miércoles, aviso; por su CP (martes), no.
            StringAssert.Contains(AvisoClienteCierraDiaReparto.Mensaje("AT", CP_MARTES, "11011", "1", "2077", "3"), "miércoles");
            Assert.IsNull(AvisoClienteCierraDiaReparto.Mensaje("AT", CP_MARTES, "11011"));
        }

        [TestMethod]
        public void SugerenciaRuta_AnotaElDiaDeLaExcepcion()
        {
            var sugerencia = new SugerenciaRutaPedidoDTO { Empresa = "1", Cliente = "2077", Contacto = "3", CodigoPostal = CP_MARTES };
            ReglasRutaPropia.AnotarDiaReparto(sugerencia, "11111", D("2026-11-02"), FinDeSemana);
            Assert.AreEqual(D("2026-11-04"), sugerencia.DiaRepartoRutaInterna);
        }

        // ================================================================ Recálculo de las fechas pendientes

        private static DateTime AjustarFijo(DateTime desde, string ruta, string almacen, string cp, string empresa, string cliente, string contacto)
        {
            DayOfWeek? dia = DiasRepartoCodigosPostales.DiaRutaInterna(ruta, cp, empresa, cliente, contacto);
            return GestorPedidosVenta.FechaEntregaAjustada(desde, ruta, almacen, Instante("2026-11-02 08:00"), CORTE_AGENCIA, (f, a) => FinDeSemana(f), dia);
        }

        [TestMethod]
        public void NuevaFechaEntrega_DelMartesAlMiercolesDeEsaSemana()
        {
            Assert.AreEqual(D("2026-11-04"), RepositorioDiasRepartoClientes.NuevaFechaEntrega(D("2026-11-03"), "AT", "ALG", CP_MARTES,
                "1", "2077", "3", D("2026-11-02"), AjustarFijo));
            Assert.AreEqual(D("2026-11-11"), RepositorioDiasRepartoClientes.NuevaFechaEntrega(D("2026-11-10"), "AT", "ALG", CP_MARTES,
                "1", "2077", "3", D("2026-11-02"), AjustarFijo));
        }

        [TestMethod]
        public void NuevaFechaEntrega_AlQuitarLaExcepcion_VuelveAlMartesDeEsaSemanaNoAlSiguiente()
        {
            DiasRepartoClientes.Reiniciar();
            Assert.AreEqual(D("2026-11-10"), RepositorioDiasRepartoClientes.NuevaFechaEntrega(D("2026-11-11"), "AT", "ALG", CP_MARTES,
                "1", "2077", "3", D("2026-11-02"), AjustarFijo));
        }

        [TestMethod]
        public void NuevaFechaEntrega_SinCambio_AgenciaRetenidaUOtroAlmacen_Null()
        {
            Assert.IsNull(RepositorioDiasRepartoClientes.NuevaFechaEntrega(D("2026-11-04"), "AT", "ALG", CP_MARTES, "1", "2077", "3", D("2026-11-02"), AjustarFijo));
            Assert.IsNull(RepositorioDiasRepartoClientes.NuevaFechaEntrega(D("2026-11-03"), "FW", "ALG", CP_MARTES, "1", "2077", "3", D("2026-11-02"), AjustarFijo));
            Assert.IsNull(RepositorioDiasRepartoClientes.NuevaFechaEntrega(new DateTime(2099, 12, 31), "AT", "ALG", CP_MARTES, "1", "2077", "3", D("2026-11-02"), AjustarFijo));
            Assert.IsNull(RepositorioDiasRepartoClientes.NuevaFechaEntrega(D("2026-11-03"), "AT", "REI", CP_MARTES, "1", "2077", "3", D("2026-11-02"), AjustarFijo));
            Assert.IsNull(RepositorioDiasRepartoClientes.NuevaFechaEntrega(D("2026-11-03"), "AT", "ALG", CP_SIN_DIA, "1", "2077", "0", D("2026-11-02"), AjustarFijo),
                "Sin día ninguno, no se toca");
        }

        // ================================================================ Controller GET/PUT api/Clientes/DiaReparto

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            foreach (string g in grupos)
            {
                claims.Add(new Claim(ClaimTypes.Role, g));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static ClientesController Controlador(IRepositorioDiasRepartoClientes repositorio, IPrincipal usuario, List<Exception> errores = null)
        {
            var controlador = new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>())
            {
                RepositorioDiasReparto = repositorio,
                RegistrarErrorDiaReparto = ex => errores?.Add(ex)
            };
            controlador.User = usuario;
            return controlador;
        }

        [TestMethod]
        public async Task Get_ConExcepcion_200ConElDia()
        {
            var repositorio = A.Fake<IRepositorioDiasRepartoClientes>();
            A.CallTo(() => repositorio.Leer("1", "2077", "3")).Returns(new ExcepcionDiaRepartoCliente
            {
                Empresa = "1", Cliente = "2077", Contacto = "3", Ruta = "03", Motivo = "Su zona es de martes", Usuario = "NUEVAVISION\\Alfredo",
                FechaModificacion = D("2026-11-03")
            });
            var ok = (OkNegotiatedContentResult<DiaRepartoClienteDTO>)await Controlador(repositorio, Usuario("Pepe")).GetDiaReparto("1", "2077 ", "3");
            Assert.AreEqual("03", ok.Content.Ruta);
            Assert.AreEqual("miércoles", ok.Content.Dia);
            Assert.AreEqual("Su zona es de martes", ok.Content.Motivo);
            Assert.AreEqual("2077", ok.Content.Cliente);
        }

        [TestMethod]
        public async Task Get_SinExcepcion_204()
        {
            var repositorio = A.Fake<IRepositorioDiasRepartoClientes>();
            A.CallTo(() => repositorio.Leer(A<string>._, A<string>._, A<string>._)).Returns((ExcepcionDiaRepartoCliente)null);
            var resultado = (StatusCodeResult)await Controlador(repositorio, Usuario("Pepe")).GetDiaReparto(null, "2077", "0");
            Assert.AreEqual(HttpStatusCode.NoContent, resultado.StatusCode);
            A.CallTo(() => repositorio.Leer("1", "2077", "0")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Put_Guarda_InvalidaYRecalcula()
        {
            var repositorio = A.Fake<IRepositorioDiasRepartoClientes>();
            A.CallTo(() => repositorio.ContactoExiste("1", "2077", "3")).Returns(true);
            A.CallTo(() => repositorio.RecalcularFechasPendientes("1", "2077", "3", "NUEVAVISION\\Alfredo")).Returns(2);
            int lecturas = 0;
            DiasRepartoClientes.Configurar(() => { lecturas++; return new ExcepcionDiaRepartoCliente[0]; });
            Assert.IsFalse(DiasRepartoClientes.HayAlguna);

            var ok = (OkNegotiatedContentResult<RespuestaDiaRepartoClienteDTO>)await Controlador(repositorio, Usuario("Alfredo", Constantes.GruposSeguridad.ALMACEN))
                .PutDiaReparto(new PeticionDiaRepartoClienteDTO { Empresa = "1", Cliente = "2077", Contacto = "3", Ruta = "03", Motivo = " Su zona es de martes " });

            Assert.AreEqual("03", ok.Content.Ruta);
            Assert.AreEqual("miércoles", ok.Content.Dia);
            Assert.AreEqual(2, ok.Content.PedidosRecalculados);
            A.CallTo(() => repositorio.Guardar(A<ExcepcionDiaRepartoCliente>.That.Matches(e =>
                e.Empresa == "1" && e.Cliente == "2077" && e.Contacto == "3" && e.Ruta == "03" && e.Motivo == "Su zona es de martes"),
                "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => repositorio.RecalcularFechasPendientes("1", "2077", "3", "NUEVAVISION\\Alfredo")).MustHaveHappened());
            _ = DiasRepartoClientes.HayAlguna;
            Assert.AreEqual(2, lecturas, "La caché se ha invalidado");
        }

        [TestMethod]
        public async Task Put_RutaNull_QuitaLaExcepcion()
        {
            var repositorio = A.Fake<IRepositorioDiasRepartoClientes>();
            A.CallTo(() => repositorio.ContactoExiste("1", "2077", "3")).Returns(true);

            var ok = (OkNegotiatedContentResult<RespuestaDiaRepartoClienteDTO>)await Controlador(repositorio, Usuario("Carlos", Constantes.GruposSeguridad.DIRECCION))
                .PutDiaReparto(new PeticionDiaRepartoClienteDTO { Empresa = "1", Cliente = "2077", Contacto = "3", Ruta = " ", Motivo = "ya no" });

            Assert.IsNull(ok.Content.Ruta);
            Assert.IsNull(ok.Content.Dia);
            A.CallTo(() => repositorio.Guardar(A<ExcepcionDiaRepartoCliente>.That.Matches(e => e.Ruta == null && e.Motivo == null), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [DataTestMethod]
        [DataRow("16")]
        [DataRow("06")]
        [DataRow("3")]
        public async Task Put_RutaQueNoEsDeLunesAViernes_400(string ruta)
        {
            var repositorio = A.Fake<IRepositorioDiasRepartoClientes>();
            A.CallTo(() => repositorio.ContactoExiste(A<string>._, A<string>._, A<string>._)).Returns(true);
            var resultado = (BadRequestErrorMessageResult)await Controlador(repositorio, Usuario("Laura", Constantes.GruposSeguridad.ADMINISTRACION))
                .PutDiaReparto(new PeticionDiaRepartoClienteDTO { Cliente = "2077", Contacto = "3", Ruta = ruta });
            Assert.AreEqual(ClientesController.MENSAJE_DIA_REPARTO_DIA_MALO, resultado.Message);
            A.CallTo(() => repositorio.Guardar(A<ExcepcionDiaRepartoCliente>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_ContactoQueNoExiste_400()
        {
            var repositorio = A.Fake<IRepositorioDiasRepartoClientes>();
            A.CallTo(() => repositorio.ContactoExiste("1", "2077", "9")).Returns(false);
            Assert.IsInstanceOfType(await Controlador(repositorio, Usuario("Alfredo", Constantes.GruposSeguridad.ALMACEN))
                .PutDiaReparto(new PeticionDiaRepartoClienteDTO { Cliente = "2077", Contacto = "9", Ruta = "03" }), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => repositorio.Guardar(A<ExcepcionDiaRepartoCliente>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_SinGrupo_403()
        {
            var repositorio = A.Fake<IRepositorioDiasRepartoClientes>();
            var resultado = (NegotiatedContentResult<string>)await Controlador(repositorio, Usuario("Pepe", "Vendedores"))
                .PutDiaReparto(new PeticionDiaRepartoClienteDTO { Cliente = "2077", Contacto = "3", Ruta = "03" });
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.Guardar(A<ExcepcionDiaRepartoCliente>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_SiFallaElRecalculo_GuardaIgualYLoTraza()
        {
            var repositorio = A.Fake<IRepositorioDiasRepartoClientes>();
            A.CallTo(() => repositorio.ContactoExiste(A<string>._, A<string>._, A<string>._)).Returns(true);
            A.CallTo(() => repositorio.RecalcularFechasPendientes(A<string>._, A<string>._, A<string>._, A<string>._))
                .ThrowsAsync(new InvalidOperationException("bloqueo"));
            var errores = new List<Exception>();

            var ok = (OkNegotiatedContentResult<RespuestaDiaRepartoClienteDTO>)await Controlador(repositorio, Usuario("Alfredo", Constantes.GruposSeguridad.ALMACEN), errores)
                .PutDiaReparto(new PeticionDiaRepartoClienteDTO { Cliente = "2077", Contacto = "3", Ruta = "03" });

            Assert.AreEqual(0, ok.Content.PedidosRecalculados);
            Assert.AreEqual(1, errores.Count);
            A.CallTo(() => repositorio.Guardar(A<ExcepcionDiaRepartoCliente>._, A<string>._)).MustHaveHappenedOnceExactly();
        }
    }
}
