using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// Caso real del 25/11/26 (job de las 8:00): las 18 líneas del 220983 (Maystar, control de pendientes) tenían el 24/11, puesto por el correo de DHL
    /// del 23/11 (aplicado). No llegó el 24 y el job las pasó al 01/12 por estadística, con el texto de «sin noticias», cuando había DOS expediciones de
    /// DHL en camino: la aplicada y la del 24/11 20:05, por asignar con el 220983 entre sus candidatos. Con un correo VIVO no es «sin noticias».
    /// </summary>
    [TestClass]
    public class FechasVencidasAgenciaEnCaminoTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 25); // miércoles
        private static readonly DateTime OCHO = HOY.AddHours(8);

        private static bool FinDeSemana(DateTime d) => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;

        private static LineaCompraVencida Linea(int pedido = 220983, int orden = 1) => new LineaCompraVencida
        {
            Empresa = "1", Pedido = pedido, Orden = orden, Proveedor = "65", NombreProveedor = "MAYSTAR, S.L.", Producto = "P" + orden,
            Descripcion = "Producto", Almacen = "ALG", Cantidad = 2, Estado = 1, FechaPrevista = new DateTime(2026, 11, 24),
            FechaPedido = new DateTime(2026, 11, 23)
        };

        private static CandidatoPedidoCompra Candidato(int pedido) =>
            new CandidatoPedidoCompra { Empresa = "1", Pedido = pedido, Proveedor = "65", Motivo = AsignadorPedidosCompra.MOTIVO_NOMBRE };

        private static CorreoSeguimientoFila Aplicado23() => new CorreoSeguimientoFila
        {
            Id = 40, MensajeId = "m40", Recibido = new DateTime(2026, 11, 23, 18, 56, 0), Agencia = "DHL", Codigo = "4313144090",
            PedidosCandidatos = AsignadorPedidosCompra.Serializar(new List<CandidatoPedidoCompra> { Candidato(220983) }),
            EmpresaAplicada = "1", PedidoAplicado = 220983, FechaRecepcionAplicada = new DateTime(2026, 11, 24)
        };

        private static CorreoSeguimientoFila PorAsignar24() => new CorreoSeguimientoFila
        {
            Id = 41, MensajeId = "m41", Recibido = new DateTime(2026, 11, 24, 20, 5, 0), Agencia = "DHL", Codigo = "4313144450",
            PedidosCandidatos = AsignadorPedidosCompra.Serializar(new List<CandidatoPedidoCompra> { Candidato(220093), Candidato(220983), Candidato(220993) })
        };

        private static List<PedidoCompraEnAire> PedidosMaystar() => new List<PedidoCompraEnAire>
        {
            new PedidoCompraEnAire { Empresa = "1", Pedido = 220093, Proveedor = "65", FechaEnvio = new DateTime(2026, 8, 20), ControlPendientes = true },
            new PedidoCompraEnAire { Empresa = "1", Pedido = 220983, Proveedor = "65", FechaEnvio = new DateTime(2026, 11, 23, 15, 44, 0), ControlPendientes = true },
            new PedidoCompraEnAire { Empresa = "1", Pedido = 220993, Proveedor = "65", FechaEnvio = new DateTime(2026, 11, 24, 12, 14, 0), ControlPendientes = true }
        };

        private IRepositorioProveedorVencido repositorio;
        private IRepositorioPlazosProveedores plazos;
        private IRepositorioCorreosSeguimiento correos;
        private List<CorreoSeguimientoFila> filas;
        private List<string> rastrosAnteriores;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioProveedorVencido>();
            plazos = A.Fake<IRepositorioPlazosProveedores>();
            correos = A.Fake<IRepositorioCorreosSeguimiento>();
            filas = new List<CorreoSeguimientoFila> { Aplicado23(), PorAsignar24() };
            rastrosAnteriores = new List<string>();
            A.CallTo(() => plazos.LeerPlazos(A<string>._)).Returns(new List<PlazoProveedor>
            {
                new PlazoProveedor { Proveedor = CalculoPlazosProveedores.PROVEEDOR_GENERICO, Recepciones = 100, DiasP80 = 9 }
            });
            // «de las 97 veces que este proveedor tardó más de 2 días, el 80 % llegó en 8 días»
            A.CallTo(() => plazos.LeerRecepcionesDeProveedores(A<string>._, A<DateTime>._, A<IEnumerable<string>>._))
                .Returns(Enumerable.Range(0, 97).Select(i => new RecepcionCompra
                {
                    Proveedor = "65", Pedido = 210000 + i, FechaPedido = new DateTime(2025, 6, 1), FechaAlbaran = new DateTime(2025, 6, 1).AddDays(3 + i % 6)
                }).ToList());
            A.CallTo(() => repositorio.LeerRecalculosAnteriores(A<DateTime>._)).ReturnsLazily(() => rastrosAnteriores);
            A.CallTo(() => repositorio.GuardarFechaRecalculada(A<LineaCompraVencida>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).Returns(true);
            A.CallTo(() => correos.TablasExisten()).Returns(true);
            A.CallTo(() => correos.AplicacionDisponible()).Returns(true);
            A.CallTo(() => correos.LeerCorreos(A<DateTime>._)).ReturnsLazily(() => Task.FromResult(filas));
            A.CallTo(() => correos.LeerPedidosEnAire(A<string>._)).ReturnsLazily(() => Task.FromResult(PedidosMaystar()));
        }

        private ServicioFechasVencidas Servicio() => new ServicioFechasVencidas(repositorio, plazos, correos, FinDeSemana, () => true,
            (empresa, productos, descripcion, causas) => { }, _ => { }, () => new TimeSpan(11, 0, 0), () => 30);

        [TestMethod]
        public void Caso220983_DhlLoTieneEnCamino_DeberiaLlegarHoy_YNoEsSinNoticias()
        {
            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY, OCHO)["1/220983/1"];

            Assert.AreEqual(OrigenFechaRecalculada.AgenciaEnCamino, r.Recalculo.Origen, "Antes: HistorialProveedor, al 01/12");
            Assert.AreEqual(HOY, r.Recalculo.FechaNueva);
            Assert.AreEqual("4313144450", r.Recalculo.EnCamino.Codigo, "El aviso más reciente: el del 24/11 20:05");
            Assert.AreEqual("Había pasado la fecha (24/11/2026); la hemos cambiado al 25/11/2026: DHL lo tiene en camino (expedición 4313144450, " +
                "aviso del 24/11): debería llegar hoy.", r.Texto);
            A.CallTo(() => repositorio.GuardarFechaRecalculada(A<LineaCompraVencida>._, HOY, RastroFechaRecalculada.USUARIO, A<string>._,
                A<string>.That.Contains("\"Origen\":\"AgenciaEnCamino\""))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void TrasLaHoraDeCorte_ElSiguienteLaborable()
        {
            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY, HOY.AddHours(12))["1/220983/1"];

            Assert.AreEqual(new DateTime(2026, 11, 26), r.Recalculo.FechaNueva);
            StringAssert.EndsWith(r.Texto, "debería llegar el 26/11/2026.");
        }

        [TestMethod]
        public void SoloElPorAsignar_TambienEsVivo_SiElPedidoNoEsDePendientes()
        {
            filas = new List<CorreoSeguimientoFila> { PorAsignar24() };

            Dictionary<string, ResultadoFechaVencida> r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea(), Linea(220093, 1) }, HOY, OCHO);

            Assert.AreEqual(OrigenFechaRecalculada.AgenciaEnCamino, r["1/220983/1"].Recalculo.Origen);
            Assert.AreNotEqual(OrigenFechaRecalculada.AgenciaEnCamino, r["1/220093/1"].Recalculo.Origen, "El de pendientes no: la estadística de siempre");
        }

        [TestMethod]
        public void AgenciaEnCamino_NoCuentaParaLlamarAlProveedor()
        {
            RecalculoFechaVencida estadistica = ReglaFechaVencida.Calcular(HOY.AddDays(-7), new DateTime(2026, 11, 17), new DateTime(2026, 11, 10), null, 9, null, null, FinDeSemana);
            rastrosAnteriores.Add(RastroFechaRecalculada.Nuevo(Linea(), estadistica, 1));
            ResultadoFechaVencida primera = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY, OCHO)["1/220983/1"];
            Assert.AreEqual(2, primera.Vencimiento);
            Assert.IsFalse(primera.HayQueLlamar, "La agencia lo tiene en camino: no hace falta llamar");

            var enCamino = new RecalculoFechaVencida { Origen = OrigenFechaRecalculada.AgenciaEnCamino, FechaNueva = HOY, FechaAnterior = HOY.AddDays(-1), FechaEstimada = HOY };
            Dictionary<string, int> cuenta = RastroFechaRecalculada.ContarPorLinea(new[] { RastroFechaRecalculada.Nuevo(Linea(), enCamino, 2) });
            Assert.IsFalse(cuenta.ContainsKey("1/220983/1"), "Un recálculo por la agencia en camino no suma vencimientos");
        }

        [TestMethod]
        public void ConEntregaEstimadaFutura_MandaEsa()
        {
            CorreoSeguimientoFila conEntrega = PorAsignar24();
            conEntrega.EntregaEstimadaDia = new DateTime(2026, 11, 27);
            filas = new List<CorreoSeguimientoFila> { Aplicado23(), conEntrega };

            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY, OCHO)["1/220983/1"];

            Assert.AreEqual(OrigenFechaRecalculada.CorreoSeguimiento, r.Recalculo.Origen);
            Assert.AreEqual(new DateTime(2026, 11, 27), r.Recalculo.FechaNueva);
        }

        [TestMethod]
        public void AvisoDeHaceMasDeUnaSemana_VuelveALaEstadistica_YLoDice()
        {
            CorreoSeguimientoFila viejo = Aplicado23();
            viejo.Recibido = new DateTime(2026, 11, 14, 10, 0, 0);
            filas = new List<CorreoSeguimientoFila> { viejo };

            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY, OCHO)["1/220983/1"];

            Assert.AreNotEqual(OrigenFechaRecalculada.AgenciaEnCamino, r.Recalculo.Origen);
            StringAssert.Contains(r.Texto, ": DHL avisó el 14/11 (expedición 4313144090) y no ha llegado; ");
        }

        [TestMethod]
        public void EnvioYaLlegado_NoEsVivo()
        {
            CorreoSeguimientoFila llegado = PorAsignar24();
            llegado.PedidoReal = 220993;
            filas = new List<CorreoSeguimientoFila> { llegado };

            ResultadoFechaVencida r = Servicio().Recalcular(new List<LineaCompraVencida> { Linea() }, HOY, OCHO)["1/220983/1"];

            Assert.AreNotEqual(OrigenFechaRecalculada.AgenciaEnCamino, r.Recalculo.Origen);
        }
    }
}
