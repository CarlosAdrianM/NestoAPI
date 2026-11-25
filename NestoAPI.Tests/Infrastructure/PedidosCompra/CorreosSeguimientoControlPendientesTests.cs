using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#897 (regla de Carlos 25/11/26): Maystar (proveedor 65) tiene control de pendientes. Tres pedidos en el aire: 220093 (cabecera del
    /// 20/08, lo que no tenían), 220983 (enviado el 23/11 15:44, 18 líneas) y 220993 (24/11 12:14, 1 línea). El correo de DHL del 24/11 20:05
    /// (expedición 4313144450, 8 bultos, 123 kg) salió «por asignar: puede ser de 3 pedidos». Va al en curso más antiguo, el 220983.
    /// </summary>
    [TestClass]
    public class CorreosSeguimientoControlPendientesTests
    {
        private static readonly DateTime CORREO = new DateTime(2026, 11, 24, 20, 5, 0);

        private static PedidoCompraEnAire Pedido(int numero, DateTime enviado, int lineas = 1, int recibidas = 0, bool control = true) => new PedidoCompraEnAire
        {
            Empresa = "1", Pedido = numero, Proveedor = "65", NombreProveedor = "MAYSTAR, S.L.", FechaEnvio = enviado, LineasPendientes = lineas,
            LineasYaRecibidas = recibidas, ControlPendientes = control
        };

        private static List<PedidoCompraEnAire> CasoMaystar() => new List<PedidoCompraEnAire>
        {
            Pedido(220093, new DateTime(2026, 8, 20, 10, 0, 0), 2),
            Pedido(220983, new DateTime(2026, 11, 23, 15, 44, 0), 18),
            Pedido(220993, new DateTime(2026, 11, 24, 12, 14, 0), 1)
        };

        private static AsignacionPedidoCompra PorNombre(IEnumerable<PedidoCompraEnAire> pedidos) => new AsignacionPedidoCompra
        {
            ProveedorPropuesto = "65",
            Candidatos = pedidos.Select(p => new CandidatoPedidoCompra { Empresa = "1", Pedido = p.Pedido, Proveedor = "65", Motivo = AsignadorPedidosCompra.MOTIVO_NOMBRE }).ToList()
        };

        [TestMethod]
        public void CasoMaystar_VaAlEnCursoMasAntiguo_YDiceElPorQue()
        {
            List<PedidoCompraEnAire> pedidos = CasoMaystar();

            AsignacionPedidoCompra a = ReglaControlPendientes.Completar(PorNombre(pedidos), pedidos, CORREO, 30);

            CandidatoPedidoCompra unico = a.Candidatos.Single();
            Assert.AreEqual(220983, unico.Pedido);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_CONTROL_PENDIENTES, unico.Motivo);
            Assert.AreEqual("MAYSTAR, S.L. tiene control de pendientes: se asigna al pedido en curso más antiguo, 220983; el 220093 es de pendientes " +
                "y el 220993 puede venir en el mismo envío.", unico.Explicacion);
            Assert.AreEqual("65", a.ProveedorPropuesto);
        }

        [TestMethod]
        public void CasoMaystarDel23_UnoDePendientesYUnoEnCurso_VaAlEnCurso()
        {
            // El correo de DHL del 23/11 18:56 (expedición 4313144090): el 220993 aún no existía
            List<PedidoCompraEnAire> pedidos = CasoMaystar().Take(2).ToList();

            AsignacionPedidoCompra a = ReglaControlPendientes.Completar(PorNombre(pedidos), pedidos, new DateTime(2026, 11, 23, 18, 56, 0), 30);

            Assert.AreEqual(220983, a.Candidatos.Single().Pedido);
            Assert.AreEqual("MAYSTAR, S.L. tiene control de pendientes: se asigna al pedido en curso 220983; el 220093 es de pendientes.", a.Candidatos.Single().Explicacion);
        }

        [TestMethod]
        public void DePendientes_PorLineasRecibidasOPorAntiguedad()
        {
            Assert.AreEqual("ya tiene 2 líneas recibidas: lo que queda es resto",
                ReglaControlPendientes.MotivoDePendientes(Pedido(1, CORREO.AddDays(-2), recibidas: 2), CORREO, 30));
            Assert.AreEqual("se envió el 20/08/2026, hace más de 30 días",
                ReglaControlPendientes.MotivoDePendientes(Pedido(2, new DateTime(2026, 8, 20)), CORREO, 30));
            Assert.IsNull(ReglaControlPendientes.MotivoDePendientes(Pedido(3, CORREO.AddDays(-29)), CORREO, 30));
            Assert.IsNotNull(ReglaControlPendientes.MotivoDePendientes(Pedido(3, CORREO.AddDays(-29)), CORREO, 10), "Los días salen del parámetro");
            var conPrimerEnvio = Pedido(4, CORREO.AddDays(-1));
            conPrimerEnvio.FechaPrimerEnvio = CORREO.AddDays(-40);
            Assert.IsNotNull(ReglaControlPendientes.MotivoDePendientes(conPrimerEnvio, CORREO, 30), "Manda el primer envío (PedidosCompraEnvios)");
        }

        [TestMethod]
        public void Parametro_SinValorOMalo_Treinta()
        {
            Assert.AreEqual(30, ReglaControlPendientes.Interpretar(null));
            Assert.AreEqual(30, ReglaControlPendientes.Interpretar("abc"));
            Assert.AreEqual(30, ReglaControlPendientes.Interpretar("0"));
            Assert.AreEqual(45, ReglaControlPendientes.Interpretar(" 45 "));
        }

        [TestMethod]
        public void NoToca_SinControlDePendientes_ConNuestroNumero_NingunoEnCurso_OEnviadoDespues()
        {
            List<PedidoCompraEnAire> sinControl = CasoMaystar().Select(p => { p.ControlPendientes = false; return p; }).ToList();
            Assert.AreEqual(3, ReglaControlPendientes.Completar(PorNombre(sinControl), sinControl, CORREO, 30).Candidatos.Count, "Lo de siempre");

            List<PedidoCompraEnAire> pedidos = CasoMaystar();
            AsignacionPedidoCompra numero = PorNombre(pedidos.Take(1));
            numero.Candidatos[0].Motivo = AsignadorPedidosCompra.MOTIVO_NUMERO;
            numero.Candidatos.Add(new CandidatoPedidoCompra { Empresa = "1", Pedido = 220983, Proveedor = "65", Motivo = AsignadorPedidosCompra.MOTIVO_NUMERO });
            Assert.AreEqual(2, ReglaControlPendientes.Completar(numero, pedidos, CORREO, 30).Candidatos.Count, "Nuestro número manda");

            List<PedidoCompraEnAire> todosDePendientes = new List<PedidoCompraEnAire> { Pedido(1, new DateTime(2026, 8, 1)), Pedido(2, CORREO.AddDays(-3), recibidas: 1) };
            Assert.AreEqual(2, ReglaControlPendientes.Completar(PorNombre(todosDePendientes), todosDePendientes, CORREO, 30).Candidatos.Count,
                "Ninguno en curso: por asignar, como hoy");

            List<PedidoCompraEnAire> despues = new List<PedidoCompraEnAire> { Pedido(1, new DateTime(2026, 8, 1)), Pedido(2, CORREO.AddHours(1)) };
            Assert.AreEqual(2, ReglaControlPendientes.Completar(PorNombre(despues), despues, CORREO, 30).Candidatos.Count,
                "Enviado después del correo: no puede ser lo que viene");
        }

        #region DHL: bultos y kilos

        internal const string DHL_MAYSTAR = "Estimado cliente:\nLe informamos de que su envío está en camino.\nRemitente: MAYSTAR S.L.\n" +
            "Expedición..: 4313144450\nBultos.....: 8\nKilos.......: 123\nRef.Cliente.: 128013\n" +
            "Siga su envío en https://www.dhl.com/es-es/home/tracking.html?tracking-id=4313144450";

        [TestMethod]
        public void Dhl_LeeBultosYKilos_YSoloLosEnsena()
        {
            BultosKilosEnvio b = BultosKilosCorreo.De("DHL", DHL_MAYSTAR);

            Assert.AreEqual(8, b.Bultos);
            Assert.AreEqual(123M, b.Kilos);
            Assert.AreEqual("8 bultos, 123 kg", BultosKilosCorreo.Texto(b));
            Assert.AreEqual(12.5M, BultosKilosCorreo.De("DHL", "Kilos: 12,5").Kilos);
            Assert.IsNull(BultosKilosCorreo.De("UPS", DHL_MAYSTAR), "Solo la agencia que sabe leerlos");
            Assert.IsNull(BultosKilosCorreo.De("DHL", "Sin datos"));
        }

        [TestMethod]
        public void Dto_TraeBultosYKilosDelExtracto()
        {
            CorreoSeguimientoCompraDTO dto = ComprasAgenciasEntradaController.Dto(new CorreoSeguimientoFila { Id = 1, Agencia = "DHL", Extracto = DHL_MAYSTAR });

            Assert.AreEqual(8, dto.Bultos);
            Assert.AreEqual(123M, dto.Kilos);
        }

        #endregion

        #region De punta a punta: el job, el reenvío y «por asignar»

        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private ILectorBuzonCompras lector;
        private LecturaBuzonCompras lectura;
        private List<Exception> errores;
        private DateTime ahora;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            errores = new List<Exception>();
            ahora = new DateTime(2026, 11, 24, 20, 30, 0);
            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns("buzon@ejemplo.test");
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(lectura));
        }

        private void PedidosMaystarEnElRepositorio()
        {
            repositorio.AnadirPedido(220093, "65", "MAYSTAR, S.L.", new DateTime(2026, 8, 20, 10, 0, 0), new DateTime(2026, 11, 10), new DateTime(2026, 11, 10));
            repositorio.AnadirPedido(220983, "65", "MAYSTAR, S.L.", new DateTime(2026, 11, 23, 15, 44, 0), new DateTime(2026, 11, 30), new DateTime(2026, 11, 30));
            repositorio.AnadirPedido(220993, "65", "MAYSTAR, S.L.", new DateTime(2026, 11, 24, 12, 14, 0), new DateTime(2026, 12, 1));
            foreach (PedidoCompraEnAire p in repositorio.Pedidos)
            {
                p.ControlPendientes = true;
            }
        }

        private async Task Pasada(params CorreoCompras[] correos)
        {
            lectura = new LecturaBuzonCompras { Correos = correos.ToList() };
            var aplicacion = new ServicioAplicacionCorreosCompras(repositorio, () => true, A.Fake<IAvisadorCorreosPorAsignar>(),
                (empresa, productos, descripcion, causas) => { }, () => ahora, errores.Add, _ => false);
            _ = await new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add, horaCorte: () => new TimeSpan(11, 0, 0), esFestivo: _ => false,
                aplicacion: aplicacion, diasPedidoDePendientes: () => 30).Pasada(ahora);
        }

        private static CorreoCompras CorreoDhl() => new CorreoCompras
        {
            Id = "g1", MensajeId = "<dhl-4313144450@ejemplo.test>", Recibido = CORREO, RemitenteDireccion = "noreply@dhl.com",
            Asunto = "Su envío DHL 4313144450 está en camino", Cuerpo = DHL_MAYSTAR
        };

        [TestMethod]
        public async Task Job_CorreoDhlDeMaystar_SeAplicaAlEnCursoMasAntiguo_ConElPorQueEnElRastro()
        {
            PedidosMaystarEnElRepositorio();

            await Pasada(CorreoDhl());

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual(220983, fila.PedidoAplicado, "Antes: «por asignar: puede ser de 3 pedidos»");
            Assert.IsTrue(repositorio.Lineas[220093].All(l => l.FechaRecepcion == new DateTime(2026, 11, 10)), "El de pendientes no se toca");
            Assert.IsTrue(repositorio.Lineas[220993].All(l => l.FechaRecepcion == new DateTime(2026, 12, 1)), "El otro en curso no se toca");
            PeticionAplicacionFecha rastro = repositorio.Rastros.Single();
            StringAssert.StartsWith(rastro.PorQuePedido, "MAYSTAR, S.L. tiene control de pendientes: se asigna al pedido en curso más antiguo, 220983");
            StringAssert.Contains(AplicacionFechaCorreoRastro(rastro), "PorQuePedido");

            string cabecera = TextosReenvioCorreoSeguimiento.Cabecera(fila, new ResultadoCorreoGuardado { Desenlace = DesenlaceCorreoSeguimiento.FechaCambiada, Pedido = 220983 },
                "noreply@dhl.com", CORREO);
            StringAssert.Contains(cabecera, "Bultos: 8 bultos, 123 kg");
            StringAssert.Contains(cabecera, "el 220093 es de pendientes y el 220993 puede venir en el mismo env&#237;o");
        }

        private static string AplicacionFechaCorreoRastro(PeticionAplicacionFecha p) => RastroAplicacionFecha.Nuevo(p, 2, null, null);

        [TestMethod]
        public void PorAsignar_MarcaLosDePendientes()
        {
            List<PedidoCompraEnAire> pedidos = CasoMaystar();
            var fila = new CorreoSeguimientoFila
            {
                Id = 7, MensajeId = "<x>", Recibido = CORREO, Agencia = "DHL", Codigo = "4313144450", Extracto = DHL_MAYSTAR,
                PedidosCandidatos = AsignadorPedidosCompra.Serializar(PorNombre(pedidos).Candidatos)
            };

            PorAsignarCorreosCompraDTO r = ComprasAgenciasEntradaController.ComponerPorAsignar(new[] { fila }, pedidos, CORREO.Date.AddDays(-7), CORREO.Date, 30);

            List<CandidatoPorAsignarDTO> candidatos = r.Envios.Single().Candidatos;
            CandidatoPorAsignarDTO dePendientes = candidatos.Single(c => c.Pedido == 220093);
            Assert.IsTrue(dePendientes.DePendientes);
            Assert.AreEqual("se envió el 20/08/2026, hace más de 30 días", dePendientes.MotivoDePendientes);
            Assert.IsFalse(candidatos.Single(c => c.Pedido == 220983).DePendientes);
            Assert.IsNull(candidatos.Single(c => c.Pedido == 220993).MotivoDePendientes);
            Assert.AreEqual(8, r.Envios.Single().Correos.Single().Bultos);
        }

        #endregion
    }
}
