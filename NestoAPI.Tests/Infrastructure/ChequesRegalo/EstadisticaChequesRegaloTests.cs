using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593 (estadística): resumen por vendedor, pendientes para la barrida telefónica, visibilidad (las reglas de
    /// «clientes para contactar») y el impulso en las sugerencias de contacto en los últimos días de la campaña.
    /// </summary>
    [TestClass]
    public class EstadisticaChequesRegaloTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 20, 10, 0, 0);

        private static CampanaEstadisticaChequeRegalo Campana(int? diasImpulso = null, bool activa = true) => new CampanaEstadisticaChequeRegalo
        {
            Codigo = "CHEQUE50_OCT_2026",
            Empresa = "1",
            Descripcion = "Cheque regalo de octubre 2026",
            ImporteBase = 50m,
            GeneraDesde = new DateTime(2026, 10, 12),
            GeneraHasta = new DateTime(2026, 10, 29),
            CanjeHasta = new DateTime(2026, 11, 7),
            Activa = activa,
            DiasImpulsoLlamadas = diasImpulso
        };

        private static ChequeRegaloVendedorFila Cheque(int id, string cliente, string vendedor, string estado = "Generado",
            int? pedido = null, bool tieneLinea = false, bool servida = false, DateTime? activacion = null) => new ChequeRegaloVendedorFila
        {
            Id = id,
            Campana = "CHEQUE50_OCT_2026",
            Empresa = "1",
            Cliente = cliente,
            Contacto = "0",
            Vendedor = vendedor,
            NombreVendedor = vendedor == null ? null : "Nombre " + vendedor,
            Nombre = "Centro " + cliente,
            Telefono = "911111111/666666666",
            Direccion = "C/ Mayor, 1",
            Poblacion = "MADRID",
            EmpresaFactura = "1",
            FacturaOrigen = "NV26" + id,
            FechaFactura = new DateTime(2026, 10, 13),
            FechaGeneracion = new DateTime(2026, 10, 13),
            FechaActivacion = activacion ?? new DateTime(2026, 10, 13),
            Estado = estado,
            PedidoCanje = pedido,
            PedidoCanjeTieneLaLinea = tieneLinea,
            LineaChequeServida = servida,
            BaseImponibleFactura = 300m,
            TotalFactura = 363m,
            UltimoContacto = new DateTime(2026, 10, 15, 12, 0, 0)
        };

        private static List<ChequeRegaloVendedorFila> Cheques() => new List<ChequeRegaloVendedorFila>
        {
            Cheque(1, "1001", "JGP"),                                                  // Disponible
            Cheque(2, "1002", "JGP", activacion: new DateTime(2026, 10, 25)),          // PendienteDeActivar
            Cheque(3, "1003", "JGP", "Canjeado", 900001, tieneLinea: true),            // EnPedido
            Cheque(4, "1004", "IM", "Canjeado", 900002, tieneLinea: true, servida: true), // Canjeado (servido)
            Cheque(5, "1005", "IM", "Caducado"),                                       // Caducado
            Cheque(6, "1006", "IM", "Anulado"),                                        // Anulado
            Cheque(7, "1007", "IM", "Canjeado", 900003, tieneLinea: false),            // Canjeado sin la línea: libre
            Cheque(8, "1008", null)                                                    // sin vendedor
        };

        // ---------------- Reglas ----------------

        [TestMethod]
        public void Resumir_SinVisibles_CuentaCadaEstadoPorVendedorYElTotal()
        {
            ResumenChequesRegaloDTO resumen = ReglasEstadisticaChequesRegalo.Resumir(Campana(), Cheques(), null, HOY);

            CollectionAssert.AreEqual(new[] { "IM", "JGP", "" }, resumen.Vendedores.Select(v => v.Vendedor).ToArray());
            ResumenChequesVendedorDTO jgp = resumen.Vendedores.Single(v => v.Vendedor == "JGP");
            Assert.AreEqual("Nombre JGP", jgp.Nombre);
            Assert.AreEqual(3, jgp.Generados);
            Assert.AreEqual(1, jgp.Usados);
            Assert.AreEqual(1, jgp.EnPedido);
            Assert.AreEqual(0, jgp.Servidos);
            Assert.AreEqual(2, jgp.Pendientes);
            Assert.AreEqual(1, jgp.PendientesDeActivar);

            ResumenChequesVendedorDTO im = resumen.Vendedores.Single(v => v.Vendedor == "IM");
            Assert.AreEqual(3, im.Generados, "el anulado no cuenta como generado");
            Assert.AreEqual(1, im.Servidos);
            Assert.AreEqual(1, im.Usados);
            Assert.AreEqual(1, im.Pendientes, "Canjeado cuyo pedido ya no tiene la línea: está libre");
            Assert.AreEqual(1, im.Caducados);
            Assert.AreEqual(1, im.Anulados);

            Assert.AreEqual("(sin vendedor)", resumen.Vendedores.Last().Nombre);
            Assert.AreEqual(7, resumen.Total.Generados);
            Assert.AreEqual(4, resumen.Total.Pendientes);
            Assert.AreEqual(2, resumen.Total.Usados);
            Assert.AreEqual(resumen.Total.Generados, resumen.Total.Usados + resumen.Total.Pendientes + resumen.Total.Caducados);
            Assert.AreEqual(18, resumen.Campana.DiasRestantes);
        }

        [TestMethod]
        public void Resumir_ConVisibles_SoloEsosVendedoresYTambienLosQueNoTienenNinguno()
        {
            var visibles = new List<VendedorVisibleDTO>
            {
                new VendedorVisibleDTO { Vendedor = "jgp", Nombre = "Javier" },
                new VendedorVisibleDTO { Vendedor = "AL", Nombre = "Alfredo" }
            };

            ResumenChequesRegaloDTO resumen = ReglasEstadisticaChequesRegalo.Resumir(Campana(), Cheques(), visibles, HOY);

            CollectionAssert.AreEqual(new[] { "AL", "JGP" }, resumen.Vendedores.Select(v => v.Vendedor).ToArray());
            Assert.AreEqual(0, resumen.Vendedores[0].Generados);
            Assert.AreEqual("Javier", resumen.Vendedores[1].Nombre);
            Assert.AreEqual(3, resumen.Total.Generados);
        }

        [TestMethod]
        public void Resumir_VendedorPedidoSinNombre_TomaElNombreDeLosCheques()
        {
            var visibles = new List<VendedorVisibleDTO> { new VendedorVisibleDTO { Vendedor = "IM" } };

            ResumenChequesRegaloDTO resumen = ReglasEstadisticaChequesRegalo.Resumir(Campana(), Cheques(), visibles, HOY);

            Assert.AreEqual("Nombre IM", resumen.Vendedores.Single().Nombre);
        }

        [TestMethod]
        public void Pendientes_SoloLosSinUsarDeLosVendedoresPedidosConLaFacturaYLosDias()
        {
            List<ChequeRegaloPendienteDTO> pendientes = ReglasEstadisticaChequesRegalo.Pendientes(Campana(), Cheques(),
                new HashSet<string>(new[] { "JGP", "IM" }), false, HOY);

            CollectionAssert.AreEquivalent(new[] { "1001", "1002", "1007" }, pendientes.Select(p => p.Cliente).ToArray());
            ChequeRegaloPendienteDTO p1001 = pendientes.Single(p => p.Cliente == "1001");
            Assert.AreEqual("Disponible", p1001.Estado);
            Assert.AreEqual(18, p1001.DiasRestantes);
            Assert.AreEqual("NV261", p1001.FacturaOrigen);
            Assert.AreEqual("1", p1001.EmpresaFactura);
            Assert.AreEqual(363m, p1001.TotalFactura);
            Assert.AreEqual(50m, p1001.Importe);
            Assert.AreEqual("911111111/666666666", p1001.Telefono);
            Assert.AreEqual(5, p1001.DiasDesdeUltimoContacto);
            Assert.AreEqual("PendienteDeActivar", pendientes.Single(p => p.Cliente == "1002").Estado);
        }

        [TestMethod]
        public void Pendientes_Todos_DevuelveCadaChequeConSuEstado()
        {
            List<ChequeRegaloPendienteDTO> todos = ReglasEstadisticaChequesRegalo.Pendientes(Campana(), Cheques(), null, true, HOY);

            Assert.AreEqual(8, todos.Count);
            Assert.AreEqual("EnPedido", todos.Single(p => p.Cliente == "1003").Estado);
            Assert.AreEqual(0, todos.Single(p => p.Cliente == "1003").DiasRestantes);
        }

        [TestMethod]
        public void Pendientes_PasadoElCanje_YaNoHayPendientes()
        {
            List<ChequeRegaloPendienteDTO> pendientes = ReglasEstadisticaChequesRegalo.Pendientes(Campana(), Cheques(), null, false, new DateTime(2026, 11, 8));

            Assert.AreEqual(0, pendientes.Count);
        }

        [TestMethod]
        public void DiasRestantes_ElUltimoDiaEsCero()
        {
            Assert.AreEqual(1, ReglasEstadisticaChequesRegalo.DiasRestantes(new DateTime(2026, 11, 7), new DateTime(2026, 11, 6, 18, 0, 0)));
            Assert.AreEqual(0, ReglasEstadisticaChequesRegalo.DiasRestantes(new DateTime(2026, 11, 7), new DateTime(2026, 11, 7, 18, 0, 0)));
            Assert.AreEqual(0, ReglasEstadisticaChequesRegalo.DiasRestantes(new DateTime(2026, 11, 7), new DateTime(2026, 11, 8)));
        }

        [TestMethod]
        public void EnImpulso_PorDefectoLosDiezUltimosDias()
        {
            CampanaEstadisticaChequeRegalo campana = Campana();

            Assert.AreEqual(new DateTime(2026, 10, 29), ReglasEstadisticaChequesRegalo.InicioImpulso(campana));
            Assert.IsFalse(ReglasEstadisticaChequesRegalo.EnImpulso(campana, new DateTime(2026, 10, 28, 23, 0, 0)));
            Assert.IsTrue(ReglasEstadisticaChequesRegalo.EnImpulso(campana, new DateTime(2026, 10, 29, 8, 0, 0)));
            Assert.IsTrue(ReglasEstadisticaChequesRegalo.EnImpulso(campana, new DateTime(2026, 11, 7, 20, 0, 0)));
            Assert.IsFalse(ReglasEstadisticaChequesRegalo.EnImpulso(campana, new DateTime(2026, 11, 8)));
        }

        [TestMethod]
        public void EnImpulso_LaCampanaMandaYCeroLoApaga()
        {
            Assert.AreEqual(new DateTime(2026, 11, 5), ReglasEstadisticaChequesRegalo.InicioImpulso(Campana(3)));
            Assert.IsFalse(ReglasEstadisticaChequesRegalo.EnImpulso(Campana(0), new DateTime(2026, 11, 7)));
            Assert.IsFalse(ReglasEstadisticaChequesRegalo.EnImpulso(Campana(activa: false), new DateTime(2026, 11, 7)));
        }

        [TestMethod]
        public void ElegirCampana_SinCodigo_LaUltimaEmpezadaHastaUnMesDespuesDelCanje()
        {
            CampanaEstadisticaChequeRegalo octubre = Campana();
            CampanaEstadisticaChequeRegalo febrero = Campana();
            febrero.Codigo = "CHEQUE_FEB_2027";
            febrero.GeneraDesde = new DateTime(2027, 2, 1);
            febrero.CanjeHasta = new DateTime(2027, 2, 28);
            var campanas = new[] { octubre, febrero };

            Assert.IsNull(ReglasEstadisticaChequesRegalo.ElegirCampana(campanas, null, new DateTime(2026, 10, 1)));
            Assert.AreSame(octubre, ReglasEstadisticaChequesRegalo.ElegirCampana(campanas, null, HOY));
            Assert.AreSame(octubre, ReglasEstadisticaChequesRegalo.ElegirCampana(campanas, null, new DateTime(2026, 12, 7)));
            Assert.IsNull(ReglasEstadisticaChequesRegalo.ElegirCampana(campanas, null, new DateTime(2026, 12, 8)));
            Assert.AreSame(febrero, ReglasEstadisticaChequesRegalo.ElegirCampana(campanas, null, new DateTime(2027, 2, 2)));
            Assert.AreSame(octubre, ReglasEstadisticaChequesRegalo.ElegirCampana(campanas, "cheque50_oct_2026", new DateTime(2027, 2, 2)));
        }

        [TestMethod]
        public void FraseImpulso_EurosSinDecimalesYDiaMes()
        {
            Assert.AreEqual("Tiene un cheque de 50 € sin usar hasta el 07/11", ReglasEstadisticaChequesRegalo.FraseImpulso(Campana()));
        }

        [TestMethod]
        public void Impulsos_SoloLosDisponiblesDelVendedorYSoloEnLosUltimosDias()
        {
            DateTime enImpulso = new DateTime(2026, 11, 2);

            List<ChequeRegaloImpulso> impulsos = ReglasEstadisticaChequesRegalo.Impulsos(Campana(), Cheques(), "jgp", enImpulso);

            CollectionAssert.AreEqual(new[] { "1001", "1002" }, impulsos.Select(i => i.Cliente).ToArray(), "el 1002 ya está activo el 02/11; el 1003 está en un pedido");
            Assert.AreEqual(new DateTime(2026, 10, 29), impulsos[0].InicioImpulso);
            Assert.AreEqual(0, ReglasEstadisticaChequesRegalo.Impulsos(Campana(), Cheques(), "JGP", HOY).Count);
        }

        [TestMethod]
        public async Task LeerImpulsos_FueraDeLosUltimosDias_NoLeeLosCheques()
        {
            IRepositorioEstadisticaChequesRegalo repositorio = A.Fake<IRepositorioEstadisticaChequesRegalo>();
            A.CallTo(() => repositorio.LeerCampanas(A<string>._)).Returns(Task.FromResult(new List<CampanaEstadisticaChequeRegalo> { Campana() }));

            List<ChequeRegaloImpulso> impulsos = await new ServicioEstadisticaChequesRegalo(repositorio).LeerImpulsos("JGP", HOY);

            Assert.AreEqual(0, impulsos.Count);
            A.CallTo(() => repositorio.LeerCheques(A<string>._)).MustNotHaveHappened();
        }

        // ---------------- Impulso en «clientes para contactar» ----------------

        private static readonly DateTime DIA_IMPULSO = new DateTime(2026, 11, 2);

        private static ClienteCarteraContacto Cartera(string cliente, string contacto, int pedidos12, int? diasContacto, float probabilidad = 0.1f,
            int? diasIntento = null, int diasPedido = 40) => new ClienteCarteraContacto
        {
            Cliente = cliente,
            Contacto = contacto,
            Nombre = "Centro " + cliente,
            Pedidos12Meses = pedidos12,
            Pedidos24Meses = Math.Max(1, pedidos12),
            UltimoPedido = DIA_IMPULSO.AddDays(-diasPedido),
            UltimoContacto = diasContacto.HasValue ? DIA_IMPULSO.AddDays(-diasContacto.Value) : (DateTime?)null,
            UltimoIntento = diasIntento.HasValue ? DIA_IMPULSO.AddDays(-diasIntento.Value) : (DateTime?)null,
            Probabilidad = probabilidad
        };

        private static ChequeRegaloImpulso Impulso(string cliente, string contacto = "0") => new ChequeRegaloImpulso
        {
            Cliente = cliente,
            Contacto = contacto,
            InicioImpulso = new DateTime(2026, 10, 29),
            CanjeHasta = new DateTime(2026, 11, 7),
            Frase = "Tiene un cheque de 50 € sin usar hasta el 07/11"
        };

        [TestMethod]
        public void Impulso_ElClienteDelChequeSubeAMaximaYElPrimeroAunqueNoLeTocara()
        {
            var cartera = new List<ClienteCarteraContacto>
            {
                Cartera("2001", "0", 6, null, 0.9f),        // Máxima por el modelo
                Cartera("2002", "0", 2, 10, diasPedido: 3)  // dentro de su cadencia y pidió hace 3 días: hoy no le toca
            };
            List<SugerenciaContactoDTO> motor = new MotorSugerenciasContacto().Priorizar(cartera, DIA_IMPULSO);
            Assert.IsFalse(motor.Any(s => s.Cliente == "2002"));

            Dictionary<string, ClienteConChequeRegalo> conCheque = ImpulsoChequesRegaloContacto.Asignar(cartera, new[] { Impulso("2002") }, DIA_IMPULSO);
            List<SugerenciaContactoDTO> lista = ImpulsoChequesRegaloContacto.Aplicar(motor, conCheque, DIA_IMPULSO);

            CollectionAssert.AreEqual(new[] { "2002", "2001" }, lista.Select(s => s.Cliente).ToArray());
            Assert.AreEqual(PrioridadesContacto.MAXIMA, lista[0].Prioridad);
            Assert.IsTrue(lista[0].TieneChequeRegalo);
            Assert.AreEqual("Tiene un cheque de 50 € sin usar hasta el 07/11", lista[0].Motivo);
            CollectionAssert.AreEqual(new[] { 1, 2 }, lista.Select(s => s.Orden).ToArray());
            Assert.IsFalse(lista[1].TieneChequeRegalo);
        }

        [TestMethod]
        public void Impulso_SiYaLeTocaba_ElMotivoLlevaLaFraseDelanteYNoSeDuplica()
        {
            var cartera = new List<ClienteCarteraContacto> { Cartera("2001", "0", 6, null, 0.9f), Cartera("2003", "0", 2, 45) };
            List<SugerenciaContactoDTO> motor = new MotorSugerenciasContacto().Priorizar(cartera, DIA_IMPULSO);

            List<SugerenciaContactoDTO> lista = ImpulsoChequesRegaloContacto.Aplicar(motor,
                ImpulsoChequesRegaloContacto.Asignar(cartera, new[] { Impulso("2003") }, DIA_IMPULSO), DIA_IMPULSO);

            CollectionAssert.AreEqual(new[] { "2003", "2001" }, lista.Select(s => s.Cliente).ToArray());
            StringAssert.StartsWith(lista[0].Motivo, "Tiene un cheque de 50 € sin usar hasta el 07/11. Ha comprado 2 veces");
        }

        [TestMethod]
        public void Impulso_SiYaSeHabloConElDesdeQueEmpezo_NoSubePeroElMotivoDiceQueTieneElCheque()
        {
            // Habló el 30/10 (después del 29/10): con 12 pedidos su cadencia es 30 días, así que hoy no le toca.
            var cartera = new List<ClienteCarteraContacto> { Cartera("2004", "0", 12, 3), Cartera("2005", "0", 2, 45) };
            List<SugerenciaContactoDTO> motor = new MotorSugerenciasContacto().Priorizar(cartera, DIA_IMPULSO);

            Dictionary<string, ClienteConChequeRegalo> conCheque = ImpulsoChequesRegaloContacto.Asignar(cartera, new[] { Impulso("2004"), Impulso("2005") }, DIA_IMPULSO);
            List<SugerenciaContactoDTO> lista = ImpulsoChequesRegaloContacto.Aplicar(motor, conCheque, DIA_IMPULSO);

            Assert.IsTrue(conCheque["2004/0"].Excluido);
            Assert.IsFalse(lista.Any(s => s.Cliente == "2004"));
            Assert.AreEqual("2005", lista[0].Cliente);
        }

        [TestMethod]
        public void Impulso_NoContestoAyer_NoSube()
        {
            var cartera = new List<ClienteCarteraContacto> { Cartera("2006", "0", 2, 45, diasIntento: 1) };

            Dictionary<string, ClienteConChequeRegalo> conCheque = ImpulsoChequesRegaloContacto.Asignar(cartera, new[] { Impulso("2006") }, DIA_IMPULSO);

            Assert.IsTrue(conCheque["2006/0"].Excluido);
        }

        [TestMethod]
        public void Impulso_ContactoDeLaFacturaFueraDeLaCartera_UsaOtroContactoDelCliente()
        {
            var cartera = new List<ClienteCarteraContacto> { Cartera("2007", "3", 2, 45), Cartera("2007", "1", 2, 45) };

            Dictionary<string, ClienteConChequeRegalo> conCheque = ImpulsoChequesRegaloContacto.Asignar(cartera, new[] { Impulso("2007", "0") }, DIA_IMPULSO);

            CollectionAssert.AreEqual(new[] { "2007/1" }, conCheque.Keys.ToArray());
        }

        [TestMethod]
        public void Impulso_ClienteQueNoEstaEnLaCartera_SeIgnora()
        {
            var cartera = new List<ClienteCarteraContacto> { Cartera("2001", "0", 6, null, 0.9f) };

            Dictionary<string, ClienteConChequeRegalo> conCheque = ImpulsoChequesRegaloContacto.Asignar(cartera, new[] { Impulso("9999") }, DIA_IMPULSO);

            Assert.AreEqual(0, conCheque.Count);
        }

        // ---------------- Controlador ----------------

        private IRepositorioEstadisticaChequesRegalo repositorio;
        private IServicioVendedoresVisibles visibles;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioEstadisticaChequesRegalo>();
            A.CallTo(() => repositorio.LeerCampanas(A<string>._)).Returns(Task.FromResult(new List<CampanaEstadisticaChequeRegalo> { Campana() }));
            A.CallTo(() => repositorio.LeerCheques("CHEQUE50_OCT_2026")).Returns(Task.FromResult(Cheques()));
            visibles = A.Fake<IServicioVendedoresVisibles>();
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private ChequesRegaloController Controlador(IPrincipal usuario) => new ChequesRegaloController(A.Fake<NVEntities>())
        {
            Request = new HttpRequestMessage(),
            Configuration = new HttpConfiguration(),
            User = usuario,
            Ahora = () => HOY,
            ServicioEstadistica = new ServicioEstadisticaChequesRegalo(repositorio),
            VendedoresVisibles = visibles
        };

        [TestMethod]
        public async Task GetResumen_Direccion_VeTodosLosVendedores()
        {
            IHttpActionResult resultado = await Controlador(Usuario("Carlos", "Dirección")).GetResumen();

            var ok = resultado as OkNegotiatedContentResult<ResumenChequesRegaloDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(3, ok.Content.Vendedores.Count);
            A.CallTo(() => visibles.LeerVisibles(A<IPrincipal>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetResumen_JefeDeVentas_SoloSuEquipo()
        {
            IPrincipal alberto = Usuario("Alberto");
            A.CallTo(() => visibles.LeerVisibles(alberto, "1")).Returns(Task.FromResult(new List<VendedorVisibleDTO>
            {
                new VendedorVisibleDTO { Vendedor = "ASH", Nombre = "Alberto" },
                new VendedorVisibleDTO { Vendedor = "JGP", Nombre = "Javier" }
            }));

            var ok = await Controlador(alberto).GetResumen() as OkNegotiatedContentResult<ResumenChequesRegaloDTO>;

            CollectionAssert.AreEqual(new[] { "ASH", "JGP" }, ok.Content.Vendedores.Select(v => v.Vendedor).ToArray());
            Assert.AreEqual(3, ok.Content.Total.Generados);
        }

        [TestMethod]
        public async Task GetPendientes_VendedorQueNoPuedeVer_Devuelve403()
        {
            IPrincipal javier = Usuario("Javier");
            A.CallTo(() => visibles.PuedeVer(javier, "1", "IM")).Returns(Task.FromResult(false));

            IHttpActionResult resultado = await Controlador(javier).GetPendientes(vendedor: "IM");

            var respuesta = resultado as ResponseMessageResult;
            Assert.IsNotNull(respuesta);
            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.Response.StatusCode);
            A.CallTo(() => repositorio.LeerCheques(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetPendientes_SuVendedor_SoloLosSuyosSinUsar()
        {
            IPrincipal javier = Usuario("Javier");
            A.CallTo(() => visibles.PuedeVer(javier, "1", "jgp")).Returns(Task.FromResult(true));

            var ok = await Controlador(javier).GetPendientes(vendedor: "jgp") as OkNegotiatedContentResult<List<ChequeRegaloPendienteDTO>>;

            CollectionAssert.AreEquivalent(new[] { "1001", "1002" }, ok.Content.Select(p => p.Cliente).ToArray());
        }

        [TestMethod]
        public async Task GetResumen_ClienteDeLaTienda_Devuelve403()
        {
            var tienda = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "clienta@correo.es"), new Claim("cliente", "1001") }, "jwt"));

            var respuesta = await Controlador(tienda).GetResumen() as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.Response.StatusCode);
        }

        [TestMethod]
        public async Task GetResumen_SinCampanaEnCurso_Devuelve404()
        {
            A.CallTo(() => repositorio.LeerCampanas(A<string>._)).Returns(Task.FromResult(new List<CampanaEstadisticaChequeRegalo>()));

            IHttpActionResult resultado = await Controlador(Usuario("Carlos", "Dirección")).GetResumen();

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }
    }
}
