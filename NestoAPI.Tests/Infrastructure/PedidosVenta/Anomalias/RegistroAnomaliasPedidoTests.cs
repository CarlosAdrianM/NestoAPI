using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta.Anomalias;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta.Anomalias
{
    /// <summary>Repositorio en memoria de PedidosAnomalias para los tests de NestoAPI#715.</summary>
    internal class RepositorioAnomaliasEnMemoria : IRepositorioAnomaliasPedido
    {
        public bool Existe { get; set; } = true;
        public Exception FalloAlLeer { get; set; }
        public Exception FalloAlInsertar { get; set; }
        public HistoricoCliente Historico { get; set; } = new HistoricoCliente();
        public ContextoGrabacion Contexto { get; set; } = new ContextoGrabacion();
        public List<FilaAnomaliaPedido> Filas { get; } = new List<FilaAnomaliaPedido>();
        public List<EstadoLineaActual> LineasActuales { get; } = new List<EstadoLineaActual>();
        public List<ModificacionCruda> Modificaciones { get; } = new List<ModificacionCruda>();
        public List<AbonoCliente> Abonos { get; } = new List<AbonoCliente>();
        public int ConsultasHistorico { get; private set; }
        public int ConsultasExiste { get; private set; }
        public List<string> UltimosProductos { get; private set; }
        private int siguienteId = 1;

        public bool TablaExiste()
        {
            ConsultasExiste++;
            return Existe;
        }

        public HistoricoCliente LeerHistorico(string empresa, string cliente, int pedidoExcluir, IEnumerable<string> productos, DateTime desde)
        {
            ConsultasHistorico++;
            UltimosProductos = productos?.ToList();
            if (FalloAlLeer != null)
            {
                throw FalloAlLeer;
            }
            return Historico;
        }

        public ContextoGrabacion LeerContexto(string empresa, string cliente, string contacto, IEnumerable<KeyValuePair<string, string>> productosAlmacen, DateTime hoy) => Contexto;

        public void Insertar(IList<FilaAnomaliaPedido> filas)
        {
            if (FalloAlInsertar != null)
            {
                throw FalloAlInsertar;
            }
            foreach (FilaAnomaliaPedido fila in filas)
            {
                fila.Id = siguienteId++;
                Filas.Add(fila);
            }
        }

        public List<FilaAnomaliaPedido> LeerPendientesEtiquetar(DateTime hasta) => Filas.Where(f => f.Resultado == null && f.Fecha < hasta).ToList();

        public List<EstadoLineaActual> LeerLineasActuales(string empresa, IEnumerable<int> pedidos)
        {
            HashSet<int> lista = new HashSet<int>(pedidos);
            return LineasActuales.Where(l => lista.Contains(l.Pedido)).ToList();
        }

        public List<ModificacionCruda> LeerModificaciones(DateTime desde, DateTime hasta, IEnumerable<int> pedidos) => Modificaciones.Where(m => m.Fecha >= desde && m.Fecha < hasta).ToList();

        public List<AbonoCliente> LeerAbonos(string empresa, IEnumerable<string> clientes, DateTime desde) => Abonos.Where(a => a.Fecha >= desde).ToList();

        public void Etiquetar(IEnumerable<FilaAnomaliaPedido> filas)
        {
            foreach (FilaAnomaliaPedido fila in filas)
            {
                FilaAnomaliaPedido guardada = Filas.Single(f => f.Id == fila.Id);
                guardada.Resultado = fila.Resultado;
                guardada.ResueltoEl = fila.ResueltoEl;
            }
        }

        public List<FilaAnomaliaPedido> LeerEntre(DateTime desde, DateTime hasta) => Filas.Where(f => f.Fecha >= desde && f.Fecha < hasta).ToList();
    }

    /// <summary>NestoAPI#715: el registro en sombra es best-effort y respeta el interruptor.</summary>
    [TestClass]
    public class RegistroAnomaliasPedidoTests
    {
        private RepositorioAnomaliasEnMemoria repositorio;
        private List<Exception> avisos;
        private List<Action> lanzados;
        private int repositoriosCreados;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioAnomaliasEnMemoria { Historico = SenalesAnomaliaPedidoTests.HistoricoHabitual() };
            avisos = new List<Exception>();
            lanzados = new List<Action>();
            repositoriosCreados = 0;
            RegistroAnomaliasPedido.CrearRepositorio = () =>
            {
                repositoriosCreados++;
                return repositorio;
            };
            RegistroAnomaliasPedido.Registrador = avisos.Add;
            RegistroAnomaliasPedido.Lanzador = trabajo => lanzados.Add(trabajo);
            RegistroAnomaliasPedido.Ahora = () => new DateTime(2026, 10, 27, 10, 0, 0);
            RegistroAnomaliasPedido.ReiniciarAvisos();
            InterruptorAnomalias.LeerAjuste = clave => "Sombra";
        }

        [TestCleanup]
        public void Limpiar()
        {
            RegistroAnomaliasPedido.CrearRepositorio = () => RepositorioAnomaliasPedidoSql.DesdeConfiguracion();
            RegistroAnomaliasPedido.Registrador = ex => ElmahHelperDummy(ex);
            RegistroAnomaliasPedido.Lanzador = trabajo => trabajo();
            RegistroAnomaliasPedido.Ahora = () => DateTime.Now;
            RegistroAnomaliasPedido.ReiniciarAvisos();
            InterruptorAnomalias.LeerAjuste = null;
        }

        private static void ElmahHelperDummy(Exception ex)
        {
        }

        private void Ejecutar()
        {
            foreach (Action trabajo in lanzados.ToList())
            {
                trabajo();
            }
        }

        [TestMethod]
        public void Registro_Apagado_NoLanzaNadaNiConsultaNada()
        {
            InterruptorAnomalias.LeerAjuste = clave => "Apagado";

            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m)), null, null, null, "NUEVAVISION\\Carlos");

            Assert.AreEqual(0, lanzados.Count);
            Assert.AreEqual(0, repositoriosCreados);
            Assert.AreEqual(0, repositorio.ConsultasExiste);
            Assert.AreEqual(0, repositorio.ConsultasHistorico);
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public void Registro_Sombra_RegistraEnSegundoPlanoYNoDevuelveNadaAlCliente()
        {
            PedidoVentaDTO pedido = SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m, 0.22m));

            // Registrar es void y no lanza: el cliente recibe exactamente lo de siempre.
            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, pedido, null, new RespuestaValidacion { ValidacionSuperada = true }, null, "NUEVAVISION\\Carlos");

            Assert.AreEqual(1, lanzados.Count, "una tarea en segundo plano");
            Assert.AreEqual(0, repositorio.Filas.Count, "antes de ejecutarse no hay nada");
            Ejecutar();

            Assert.AreEqual(1, repositorio.ConsultasHistorico);
            CollectionAssert.AreEquivalent(new List<string> { "38406" }, repositorio.UltimosProductos);
            FilaAnomaliaPedido contexto = repositorio.Filas.Single(f => f.Regla == ReglasAnomalia.CONTEXTO);
            Assert.AreEqual(929057, contexto.Pedido);
            Assert.AreEqual("10257", contexto.Cliente);
            Assert.AreEqual(AplicacionesOrigen.NESTO, contexto.Aplicacion);
            Assert.AreEqual(OperacionesAnomalia.CREAR, contexto.Operacion);
            Assert.IsFalse(contexto.Mostrado);
            Assert.IsNull(contexto.Respuesta);
            Assert.AreEqual(ReglasAnomalia.VERSION_REGLAS, contexto.VersionModelo);
            Assert.AreEqual(613.50m, contexto.ImportePedido);
            Assert.AreEqual(1, contexto.NumeroLineas);
            Assert.IsTrue(repositorio.Filas.Any(f => f.Regla == ReglasAnomalia.CANTIDAD_ALTA && f.CantidadGrabada == 30 && f.PrecioGrabado == 20.45m && f.DescuentoGrabado == 0.22m));
            Assert.IsTrue(repositorio.Filas.Any(f => f.Regla == ReglasAnomalia.IMPORTE_PEDIDO && f.Linea == null));
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public void Registro_Activo_EnElCorteUnoSeComportaComoSombra()
        {
            InterruptorAnomalias.LeerAjuste = clave => "Activo";

            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m)), null, null, null, null);
            Ejecutar();

            Assert.IsTrue(repositorio.Filas.Any(f => f.Regla == ReglasAnomalia.CANTIDAD_ALTA));
        }

        [TestMethod]
        public void Registro_PedidoNormal_SoloLaFilaDeContexto()
        {
            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 3, 20.45m, 0.22m)), null, null, null, "NUEVAVISION\\Carlos");
            Ejecutar();

            Assert.AreEqual(1, repositorio.Filas.Count);
            Assert.AreEqual(ReglasAnomalia.CONTEXTO, repositorio.Filas[0].Regla);
        }

        [TestMethod]
        public void Registro_ContextoDeGrabacion_DeudaFormaPagoYStock()
        {
            repositorio.Contexto = new ContextoGrabacion { DeudaTotal = 112.95m, DeudaVencida = 50m, DiasVencidoMasAntiguo = 12, FormaPagoFicha = "TRN", StockLeido = true };
            repositorio.Contexto.Stock[ContextoGrabacion.Clave("38406", "ALG")] = 7;
            PedidoVentaDTO pedido = SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m, 0.22m));
            pedido.formaPago = "RCB";

            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.MODIFICAR, pedido, new List<LineaGrabadaAnomalia>(), null, "NestoApp", "carlos");
            Ejecutar();

            FilaAnomaliaPedido contexto = repositorio.Filas.Single(f => f.Regla == ReglasAnomalia.CONTEXTO);
            Assert.AreEqual(112.95m, contexto.DeudaTotal);
            Assert.AreEqual(50m, contexto.DeudaVencida);
            Assert.AreEqual(12, contexto.DiasVencidoMasAntiguo);
            Assert.AreEqual("TRN", contexto.FormaPagoFicha);
            Assert.AreEqual("RCB", contexto.FormaPagoPedido);
            Assert.AreEqual("NestoApp", contexto.Aplicacion, "la cabecera X-Aplicacion manda");
            Assert.AreEqual(OperacionesAnomalia.MODIFICAR, contexto.Operacion);
            FilaAnomaliaPedido sinStock = repositorio.Filas.Single(f => f.Regla == ReglasAnomalia.SIN_STOCK);
            Assert.AreEqual(7, sinStock.StockAlGrabar);
            Assert.AreEqual(7m, sinStock.Valor);
            Assert.AreEqual(30m, sinStock.Esperado);
            Assert.AreEqual(7, repositorio.Filas.Single(f => f.Regla == ReglasAnomalia.CANTIDAD_ALTA).StockAlGrabar);
        }

        [TestMethod]
        public void Registro_SinTabla_AvisaUnaVezPorProcesoYNoRompe()
        {
            repositorio.Existe = false;
            PedidoVentaDTO pedido = SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m));

            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, pedido, null, null, null, null);
            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, pedido, null, null, null, null);
            Ejecutar();

            Assert.AreEqual(0, repositorio.Filas.Count);
            Assert.AreEqual(0, repositorio.ConsultasHistorico, "sin tabla no se lee el histórico");
            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, "Issue715_PedidosAnomalias.sql");
        }

        [TestMethod]
        public void Registro_FalloAlLeerOInsertar_AvisaUnaVezYNoRompe()
        {
            repositorio.FalloAlLeer = new InvalidOperationException("se cayó la BD");
            PedidoVentaDTO pedido = SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m));

            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, pedido, null, null, null, null);
            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, pedido, null, null, null, null);
            Ejecutar();

            Assert.AreEqual(0, repositorio.Filas.Count);
            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, "El pedido está grabado igual");
            Assert.AreSame(repositorio.FalloAlLeer, avisos[0].InnerException);
        }

        [TestMethod]
        public void Registro_RepositorioQueNoSePuedeCrear_NoRompe()
        {
            RegistroAnomaliasPedido.CrearRepositorio = () => throw new InvalidOperationException("sin cadena de conexión");

            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m)), null, null, null, null);
            Ejecutar();

            Assert.AreEqual(1, avisos.Count);
        }

        [TestMethod]
        public void Registro_PedidoNuloOSinCliente_NoHaceNada()
        {
            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, null, null, null, null, null);
            PedidoVentaDTO sinCliente = SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m));
            sinCliente.cliente = null;
            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, sinCliente, null, null, null, null);
            Ejecutar();

            Assert.AreEqual(0, repositorio.Filas.Count);
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public void Registro_LineasNuevas_TomanElNumeroDeOrdenDeLoGrabado()
        {
            repositorio.LineasActuales.Add(new EstadoLineaActual { Pedido = 929057, Linea = 1, Producto = "38406", Cantidad = 30, Precio = 20.45m });
            repositorio.LineasActuales.Add(new EstadoLineaActual { Pedido = 929057, Linea = 2, Producto = "25720", Cantidad = 1, Precio = 15.95m });
            PedidoVentaDTO pedido = SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m, 0.22m), SenalesAnomaliaPedidoTests.Linea("25720", 1, 15.95m));

            RegistroAnomaliasPedido.Registrar(OperacionesAnomalia.CREAR, pedido, null, null, null, null);
            Ejecutar();

            Assert.AreEqual(1, repositorio.Filas.Single(f => f.Regla == ReglasAnomalia.CANTIDAD_ALTA).Linea);
        }

        [TestMethod]
        public void ResolverLineasNuevas_PorProductoCantidadYPrecio_OUnicaCandidata()
        {
            PedidoVentaDTO pedido = SenalesAnomaliaPedidoTests.Pedido(
                SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m),
                SenalesAnomaliaPedidoTests.Linea("38406", 3, 20.45m),
                SenalesAnomaliaPedidoTests.Linea("25720", 1, 15.95m),
                SenalesAnomaliaPedidoTests.Linea("99999", 1, 1m));
            InstantaneaAnomalias instantanea = SenalesAnomaliaPedidoTests.Instantanea(pedido);
            var grabadas = new List<EstadoLineaActual>
            {
                new EstadoLineaActual { Pedido = 929057, Linea = 1, Producto = "38406", Cantidad = 3, Precio = 20.45m },
                new EstadoLineaActual { Pedido = 929057, Linea = 2, Producto = "38406", Cantidad = 30, Precio = 20.45m },
                new EstadoLineaActual { Pedido = 929057, Linea = 3, Producto = "25720", Cantidad = 2, Precio = 15.95m }
            };

            RegistroAnomaliasPedido.ResolverLineasNuevas(instantanea, grabadas);

            Assert.AreEqual(2, instantanea.Lineas[0].Linea);
            Assert.AreEqual(1, instantanea.Lineas[1].Linea);
            Assert.AreEqual(3, instantanea.Lineas[2].Linea, "única candidata del producto aunque cambie la cantidad");
            Assert.AreEqual(0, instantanea.Lineas[3].Linea, "sin candidata se queda a 0");
        }

        [TestMethod]
        public void Instantanea_DesdeLinPedidoVta_ReduceLoQueMiranLasReglas()
        {
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 7, Producto = "38406          ", Cantidad = 3, Precio = 20.45m, Aplicar_Dto = true, DescuentoCliente = 0, DescuentoProducto = 0.22m, Descuento = 0 },
                new LinPedidoVta { Nº_Orden = 8, Producto = "25720", Cantidad = 1, Precio = 15.95m, Aplicar_Dto = false, DescuentoProducto = 0.5m, Descuento = 0.1m },
                null
            };

            List<LineaGrabadaAnomalia> antes = RegistroAnomaliasPedido.Instantanea(lineas);

            Assert.AreEqual(2, antes.Count);
            Assert.AreEqual("38406", antes[0].Producto);
            Assert.AreEqual(0.22m, antes[0].Descuento);
            Assert.AreEqual(0.1m, antes[1].Descuento, "sin Aplicar Dto solo cuenta el de la línea");
        }

        [TestMethod]
        public void ConstruirFilas_SenalDeLineaNueva_SinNumeroDeOrden()
        {
            InstantaneaAnomalias instantanea = SenalesAnomaliaPedidoTests.Instantanea(SenalesAnomaliaPedidoTests.Pedido(SenalesAnomaliaPedidoTests.Linea("38406", 30, 20.45m)));
            var senal = new SenalAnomalia { Regla = ReglasAnomalia.CANTIDAD_ALTA, Linea = 0, Producto = "38406", Valor = 30, Esperado = 13, Puntuacion = 2.3m };

            List<FilaAnomaliaPedido> filas = RegistroAnomaliasPedido.ConstruirFilas(instantanea, new[] { senal }, null);

            Assert.AreEqual(2, filas.Count);
            FilaAnomaliaPedido fila = filas[1];
            Assert.IsNull(fila.Linea);
            Assert.AreEqual("38406", fila.Producto);
            Assert.AreEqual(30, fila.CantidadGrabada);
            Assert.IsNull(fila.StockAlGrabar, "sin lectura de stock queda NULL");
            Assert.IsNull(fila.DeudaTotal);
        }
    }
}
