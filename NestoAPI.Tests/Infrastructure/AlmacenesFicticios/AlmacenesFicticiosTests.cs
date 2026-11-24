using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.AlmacenesFicticios;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.AlmacenesFicticios
{
    /// <summary>
    /// NestoAPI#894: almacenes ficticios de los vendedores. La regla de quién ve y quién mete y saca (contra qué almacén), las
    /// validaciones del traspaso, los huecos de Algete, el SQL y el servicio con el repositorio falseado.
    /// </summary>
    [TestClass]
    public class AlmacenesFicticiosTests
    {
        private static readonly List<DuenoAlmacenFicticio> DUENOS = new List<DuenoAlmacenFicticio>
        {
            new DuenoAlmacenFicticio { Usuario = "Israel", Almacen = "IF", Vendedor = "IF" },
            new DuenoAlmacenFicticio { Usuario = "Sancho", Almacen = "AB", Vendedor = "ASH" },
            new DuenoAlmacenFicticio { Usuario = "Irene", Almacen = "IM", Vendedor = "IM" }
        };

        private static ContextoUsuarioFicticio Vendedor(string usuario, string vendedor, params string[] equipo) => new ContextoUsuarioFicticio
        {
            Usuario = usuario,
            Vendedor = vendedor,
            Equipo = new[] { vendedor }.Concat(equipo).ToList()
        };

        // ---------------------------------------------------------------- LA REGLA: ver

        [TestMethod]
        public void Decidir_ElVendedorVeElSuyoPorUsuario_YNoElDeOtro()
        {
            ContextoUsuarioFicticio israel = Vendedor("Israel", null);
            Assert.IsTrue(ReglasAlmacenesFicticios.Decidir(israel, "IF", DUENOS).PuedeVer);
            Assert.IsTrue(ReglasAlmacenesFicticios.Decidir(israel, "IF", DUENOS).EsSuyo);
            Assert.IsFalse(ReglasAlmacenesFicticios.Decidir(israel, "IM", DUENOS).PuedeVer);
            Assert.IsFalse(ReglasAlmacenesFicticios.Decidir(israel, "PRV", DUENOS).PuedeVer);
        }

        [TestMethod]
        public void Decidir_ElVendedorDeNestoAppVeElSuyoPorSuVendedor()
        {
            // NestoApp: el usuario de Identity no es el de ParametrosUsuario, pero el claim «Vendedor» sí casa
            ContextoUsuarioFicticio app = Vendedor("israel@nuevavision.es", "IF");
            DecisionAlmacenFicticio decision = ReglasAlmacenesFicticios.Decidir(app, "IF", DUENOS);
            Assert.IsTrue(decision.PuedeVer);
            Assert.IsTrue(decision.EsSuyo);
            Assert.IsFalse(decision.PuedeTraspasar, "un vendedor solo consulta");
        }

        [TestMethod]
        public void Decidir_ElJefeDeEquipoVeLosDeSuEquipo_YNoLosDeOtroEquipo()
        {
            ContextoUsuarioFicticio sancho = Vendedor("Sancho", "ASH", "IF");
            Assert.IsTrue(ReglasAlmacenesFicticios.Decidir(sancho, "AB", DUENOS).EsSuyo);
            DecisionAlmacenFicticio deIsrael = ReglasAlmacenesFicticios.Decidir(sancho, "IF", DUENOS);
            Assert.IsTrue(deIsrael.PuedeVer);
            Assert.IsFalse(deIsrael.EsSuyo);
            Assert.IsFalse(ReglasAlmacenesFicticios.Decidir(sancho, "IM", DUENOS).PuedeVer);
        }

        [TestMethod]
        public void Decidir_DireccionEInformaticaVenTodos_PeroNoMetenNiSacan()
        {
            foreach (var usuario in new[] { new ContextoUsuarioFicticio { EsDireccion = true }, new ContextoUsuarioFicticio { EsInformatica = true } })
            {
                foreach (string almacen in new[] { "IF", "PRV", "AMZ", "SDC" })
                {
                    DecisionAlmacenFicticio d = ReglasAlmacenesFicticios.Decidir(usuario, almacen, DUENOS);
                    Assert.IsTrue(d.PuedeVer, almacen);
                    Assert.IsFalse(d.PuedeTraspasar, almacen);
                }
            }
        }

        [TestMethod]
        public void Decidir_ElClienteDeLaTiendaNoVeNada()
        {
            DecisionAlmacenFicticio d = ReglasAlmacenesFicticios.Decidir(new ContextoUsuarioFicticio { EsClienteTienda = true, EsDireccion = true }, "IF", DUENOS);
            Assert.IsFalse(d.PuedeVer);
            Assert.IsFalse(d.PuedeTraspasar);
        }

        // ---------------------------------------------------------------- LA REGLA: meter y sacar, contra qué almacén

        [TestMethod]
        public void Decidir_AlmacenMueveTodosContraAlgete()
        {
            var almacen = new ContextoUsuarioFicticio { EsAlmacen = true };
            foreach (string codigo in new[] { "IF", "PRV", "EXT", "AMZ", "SDC" })
            {
                DecisionAlmacenFicticio d = ReglasAlmacenesFicticios.Decidir(almacen, codigo, DUENOS);
                Assert.IsTrue(d.PuedeTraspasar, codigo);
                Assert.AreEqual("ALG", d.AlmacenPropio, codigo);
            }
        }

        [TestMethod]
        public void Decidir_LaTiendaMueveLosDeVendedorContraLaSuya()
        {
            var reina = new ContextoUsuarioFicticio { EsTiendas = true, AlmacenTienda = "REI" };
            DecisionAlmacenFicticio d = ReglasAlmacenesFicticios.Decidir(reina, "IF", DUENOS);
            Assert.IsTrue(d.PuedeTraspasar);
            Assert.AreEqual("REI", d.AlmacenPropio);
            // una tienda que además es de Almacén: contra la suya
            Assert.AreEqual("ALC", ReglasAlmacenesFicticios.Decidir(new ContextoUsuarioFicticio { EsTiendas = true, EsAlmacen = true, AlmacenTienda = "ALC" }, "IF", DUENOS).AlmacenPropio);
        }

        [TestMethod]
        public void Decidir_LaTiendaSinTiendaAsignadaNoMueve()
        {
            DecisionAlmacenFicticio d = ReglasAlmacenesFicticios.Decidir(new ContextoUsuarioFicticio { EsTiendas = true }, "IF", DUENOS);
            Assert.IsTrue(d.PuedeVer);
            Assert.IsFalse(d.PuedeTraspasar);
            StringAssert.Contains(d.MotivoNoPuedeTraspasar, "AlmacénPedidoVta");
        }

        [TestMethod]
        public void Decidir_PrvYExtSoloAlmacen_LaTiendaSoloConsulta()
        {
            var reina = new ContextoUsuarioFicticio { EsTiendas = true, AlmacenTienda = "REI" };
            foreach (string codigo in new[] { "PRV", "EXT" })
            {
                DecisionAlmacenFicticio d = ReglasAlmacenesFicticios.Decidir(reina, codigo, DUENOS);
                Assert.AreEqual(TipoAlmacenFicticio.SoloAlmacen, d.Tipo);
                Assert.IsTrue(d.PuedeVer);
                Assert.IsFalse(d.PuedeTraspasar);
                StringAssert.Contains(d.MotivoNoPuedeTraspasar, "solo lo hacen los de Almacén");
            }
        }

        [TestMethod]
        public void Decidir_AmzLoMuevenAlmacenYLaTiendaOnline_ContraAlgete()
        {
            var online = new ContextoUsuarioFicticio { EsTiendaOnline = true };
            DecisionAlmacenFicticio amz = ReglasAlmacenesFicticios.Decidir(online, "AMZ", DUENOS);
            Assert.AreEqual(TipoAlmacenFicticio.Amazon, amz.Tipo);
            Assert.IsTrue(amz.PuedeVer);
            Assert.IsTrue(amz.PuedeTraspasar);
            Assert.AreEqual("ALG", amz.AlmacenPropio);
            Assert.IsFalse(ReglasAlmacenesFicticios.Decidir(online, "IF", DUENOS).PuedeVer, "la tienda online solo ve AMZ");
            Assert.IsFalse(ReglasAlmacenesFicticios.Decidir(new ContextoUsuarioFicticio { EsTiendas = true, AlmacenTienda = "REI" }, "AMZ", DUENOS).PuedeTraspasar);
        }

        [TestMethod]
        public void Decidir_LosQueNoSonDeNadieSonDeConsultaSalvoAlmacen()
        {
            Assert.AreEqual(TipoAlmacenFicticio.Otro, ReglasAlmacenesFicticios.Tipo("SDC", DUENOS));
            DecisionAlmacenFicticio d = ReglasAlmacenesFicticios.Decidir(new ContextoUsuarioFicticio { EsTiendas = true, AlmacenTienda = "REI" }, "SDC", DUENOS);
            Assert.IsTrue(d.PuedeVer);
            Assert.IsFalse(d.PuedeTraspasar);
        }

        [TestMethod]
        public void Tipo_UnAlmacenConDuenoEsDeVendedor()
        {
            Assert.AreEqual(TipoAlmacenFicticio.Vendedor, ReglasAlmacenesFicticios.Tipo("if", DUENOS));
            Assert.AreEqual(TipoAlmacenFicticio.Otro, ReglasAlmacenesFicticios.Tipo("DLS", DUENOS), "sin parámetro todavía: no es de nadie");
        }

        // ---------------------------------------------------------------- validaciones

        private static TraspasoAlmacenFicticioDTO Peticion(string sentido = "Entregar", params LineaTraspasoFicticioDTO[] lineas) => new TraspasoAlmacenFicticioDTO
        {
            AlmacenFicticio = "IF",
            Sentido = sentido,
            Lineas = lineas.Any() ? lineas.ToList() : new List<LineaTraspasoFicticioDTO> { new LineaTraspasoFicticioDTO { Producto = "45958", Cantidad = 3 } }
        };

        [TestMethod]
        public void MotivoPeticionNoValida_ValeUnaPeticionNormal()
        {
            Assert.IsNull(ReglasAlmacenesFicticios.MotivoPeticionNoValida(Peticion()));
            Assert.IsNull(ReglasAlmacenesFicticios.MotivoPeticionNoValida(Peticion("recoger")));
        }

        [TestMethod]
        public void MotivoPeticionNoValida_RechazaLoQueNoVale()
        {
            Assert.IsNotNull(ReglasAlmacenesFicticios.MotivoPeticionNoValida(null));
            Assert.IsNotNull(ReglasAlmacenesFicticios.MotivoPeticionNoValida(Peticion("Alta")));
            StringAssert.Contains(ReglasAlmacenesFicticios.MotivoPeticionNoValida(Peticion("Entregar", new LineaTraspasoFicticioDTO { Producto = "1", Cantidad = 0 })), "mayor que cero");
            StringAssert.Contains(ReglasAlmacenesFicticios.MotivoPeticionNoValida(Peticion("Entregar", new LineaTraspasoFicticioDTO { Producto = "1", Cantidad = -2 })), "mayor que cero");
            StringAssert.Contains(ReglasAlmacenesFicticios.MotivoPeticionNoValida(Peticion("Entregar", new LineaTraspasoFicticioDTO { Producto = " ", Cantidad = 1 })), "sin producto");
            StringAssert.Contains(ReglasAlmacenesFicticios.MotivoPeticionNoValida(Peticion("Entregar", new LineaTraspasoFicticioDTO { Producto = "1", Cantidad = 1, Pasillo = "002" })), "pasillo, fila y columna");
            TraspasoAlmacenFicticioDTO sinAlmacen = Peticion();
            sinAlmacen.AlmacenFicticio = null;
            Assert.IsNotNull(ReglasAlmacenesFicticios.MotivoPeticionNoValida(sinAlmacen));
            TraspasoAlmacenFicticioDTO comentarioLargo = Peticion();
            comentarioLargo.Comentario = new string('x', 501);
            Assert.IsNotNull(ReglasAlmacenesFicticios.MotivoPeticionNoValida(comentarioLargo));
        }

        private static readonly Dictionary<string, ProductoTraspasoFicticio> PRODUCTOS = new Dictionary<string, ProductoTraspasoFicticio>(StringComparer.OrdinalIgnoreCase)
        {
            ["45958"] = new ProductoTraspasoFicticio { Producto = "45958", Nombre = "CREMA", Estado = 0, Ubicar = true },
            ["17404"] = new ProductoTraspasoFicticio { Producto = "17404", Nombre = "ANULADO", Estado = -1, Ubicar = true },
            ["FIC"] = new ProductoTraspasoFicticio { Producto = "FIC", Nombre = "PORTES", Ficticio = true },
            ["SINHUECO"] = new ProductoTraspasoFicticio { Producto = "SINHUECO", Nombre = "SIN UBICAR", Ubicar = false }
        };

        [TestMethod]
        public void MotivosProductos_NoExisteFicticioYAnuladoSoloAlEntregar()
        {
            List<string> entregar = ReglasAlmacenesFicticios.MotivosProductos(new[] { "45958", "99999", "FIC", "17404" }, PRODUCTOS, SentidosTraspasoFicticio.ENTREGAR);
            Assert.AreEqual(3, entregar.Count);
            Assert.IsTrue(entregar.Any(m => m.Contains("No existe el producto 99999")));
            Assert.IsTrue(entregar.Any(m => m.Contains("ficticio")));
            Assert.IsTrue(entregar.Any(m => m.Contains("anulado")));
            // recoger lo anulado sí vale: es lo que hay que sacar del vendedor
            Assert.AreEqual(0, ReglasAlmacenesFicticios.MotivosProductos(new[] { "17404" }, PRODUCTOS, SentidosTraspasoFicticio.RECOGER).Count);
        }

        [TestMethod]
        public void MotivosStock_NoSeDejaEnNegativoSalvoQueElAlmacenLoPermita()
        {
            var pedidas = new Dictionary<string, int> { ["45958"] = 5 };
            var stock = new Dictionary<string, int> { ["45958"] = 3 };
            List<string> motivos = ReglasAlmacenesFicticios.MotivosStock(pedidas, stock, false, "IF", true, PRODUCTOS);
            Assert.AreEqual(1, motivos.Count);
            StringAssert.Contains(motivos[0], "En IF solo hay 3 de 45958 (CREMA), no 5");
            Assert.AreEqual(0, ReglasAlmacenesFicticios.MotivosStock(pedidas, stock, true, "IF", true, PRODUCTOS).Count);
            Assert.AreEqual(0, ReglasAlmacenesFicticios.MotivosStock(pedidas, new Dictionary<string, int> { ["45958"] = 5 }, false, "ALG", false, PRODUCTOS).Count);
            StringAssert.Contains(ReglasAlmacenesFicticios.MotivosStock(pedidas, new Dictionary<string, int>(), false, "REI", false, PRODUCTOS)[0], "En REI solo hay 0");
        }

        [TestMethod]
        public void UnidadesPorProducto_SumaLasLineasDeUnMismoProducto()
        {
            Dictionary<string, int> u = ReglasAlmacenesFicticios.UnidadesPorProducto(new[]
            {
                new LineaTraspasoFicticioDTO { Producto = "45958 ", Cantidad = 2 },
                new LineaTraspasoFicticioDTO { Producto = "45958", Cantidad = 1 },
                new LineaTraspasoFicticioDTO { Producto = "17404", Cantidad = 4 }
            });
            Assert.AreEqual(3, u["45958"]);
            Assert.AreEqual(4, u["17404"]);
        }

        // ---------------------------------------------------------------- huecos de Algete

        private static readonly List<HuecoLibreFicticio> HUECOS = new List<HuecoLibreFicticio>
        {
            new HuecoLibreFicticio { Producto = "45958", Pasillo = "002", Fila = "002", Columna = "004", Libres = 2, Desde = new DateTime(2026, 1, 1) },
            new HuecoLibreFicticio { Producto = "45958", Pasillo = "003", Fila = "001", Columna = "001", Libres = 10, Desde = new DateTime(2026, 5, 1) }
        };

        [TestMethod]
        public void AsignarHuecos_SinHuecoSeProponeElMasAntiguoQueTengaParaTodaLaLinea()
        {
            List<LineaTraspasoFicticioPlan> plan = ReglasAlmacenesFicticios.AsignarHuecos(
                new[] { new LineaTraspasoFicticioDTO { Producto = "45958", Cantidad = 3 } }, HUECOS, p => true, "ALG", out List<string> motivos);
            Assert.AreEqual(0, motivos.Count);
            Assert.AreEqual("003/001/001", plan[0].Hueco.Texto, "el 002/002/004 solo tiene 2");
            Assert.IsTrue(plan[0].HuecoPropuesto);

            plan = ReglasAlmacenesFicticios.AsignarHuecos(new[] { new LineaTraspasoFicticioDTO { Producto = "45958", Cantidad = 2 } }, HUECOS, p => true, "ALG", out motivos);
            Assert.AreEqual("002/002/004", plan[0].Hueco.Texto, "FIFO");
        }

        [TestMethod]
        public void AsignarHuecos_ElHuecoElegidoTieneQueTenerLoPedido_DescontandoLasLineasAnteriores()
        {
            var lineas = new[]
            {
                new LineaTraspasoFicticioDTO { Producto = "45958", Cantidad = 2, Pasillo = "002", Fila = "002", Columna = "004" },
                new LineaTraspasoFicticioDTO { Producto = "45958", Cantidad = 1, Pasillo = "002", Fila = "002", Columna = "004" }
            };
            _ = ReglasAlmacenesFicticios.AsignarHuecos(lineas, HUECOS, p => true, "ALG", out List<string> motivos);
            Assert.AreEqual(1, motivos.Count);
            StringAssert.Contains(motivos[0], "En el hueco 002/002/004 hay 0 libres de 45958, no 1");
            StringAssert.Contains(motivos[0], "003/001/001 (10)");
        }

        [TestMethod]
        public void AsignarHuecos_SinNadaLibreSeDice_YLosQueNoSeUbicanNoLlevanHueco()
        {
            List<LineaTraspasoFicticioPlan> plan = ReglasAlmacenesFicticios.AsignarHuecos(new[]
            {
                new LineaTraspasoFicticioDTO { Producto = "SINHUECO", Cantidad = 1 },
                new LineaTraspasoFicticioDTO { Producto = "17404", Cantidad = 1 }
            }, HUECOS, p => p != "SINHUECO", "ALG", out List<string> motivos);
            Assert.IsNull(plan[0].Hueco);
            Assert.AreEqual(1, motivos.Count);
            StringAssert.Contains(motivos[0], "no hay nada libre de 17404");
        }

        [TestMethod]
        public void PlanificadorSalidaHueco_TodoONada_YLoMasAntiguoPrimero()
        {
            var libres = new List<FilaUbicacionLibre>
            {
                new FilaUbicacionLibre { NumeroOrden = 2, Producto = "1", Pasillo = "002", Fila = "002", Columna = "004", Cantidad = 3, Estado = 0, FechaCreacion = new DateTime(2026, 2, 1) },
                new FilaUbicacionLibre { NumeroOrden = 1, Producto = "1", Pasillo = "002", Fila = "002", Columna = "004", Cantidad = 2, Estado = 0, FechaCreacion = new DateTime(2026, 1, 1) }
            };
            List<TomaDeHueco> tomas = PlanificadorSalidaHueco.Repartir(libres, 4);
            Assert.AreEqual(2, tomas.Count);
            Assert.AreEqual(1, tomas[0].Libre.NumeroOrden);
            Assert.IsTrue(tomas[0].Entera);
            Assert.AreEqual(2, tomas[1].Cantidad);
            Assert.IsNull(PlanificadorSalidaHueco.Repartir(libres, 6));
        }

        // ---------------------------------------------------------------- extracto y textos

        [TestMethod]
        public void LineasExtracto_UnaParejaPorProducto_MenosEnElOrigenYMasEnElDestino()
        {
            var unidades = new Dictionary<string, int> { ["45958"] = 3 };
            List<LineaMovimientoTraspaso> entregar = ReglasAlmacenesFicticios.LineasExtracto(SentidosTraspasoFicticio.ENTREGAR, "ALC", "IF", unidades, PRODUCTOS);
            Assert.AreEqual(2, entregar.Count);
            Assert.AreEqual("ALC", entregar[0].Almacen);
            Assert.AreEqual(-3, entregar[0].Cantidad);
            Assert.AreEqual("IF", entregar[1].Almacen);
            Assert.AreEqual(3, entregar[1].Cantidad);
            Assert.AreEqual("CREMA", entregar[0].Texto, "como Nesto viejo: el nombre del producto");

            List<LineaMovimientoTraspaso> recoger = ReglasAlmacenesFicticios.LineasExtracto(SentidosTraspasoFicticio.RECOGER, "ALG", "IF", unidades, PRODUCTOS);
            Assert.AreEqual("IF", recoger[0].Almacen);
            Assert.AreEqual(-3, recoger[0].Cantidad);
            Assert.AreEqual("ALG", recoger[1].Almacen);
            Assert.AreEqual(3, recoger[1].Cantidad);
        }

        [TestMethod]
        public void Mensaje_DeUsted_ConElTraspasoYLoPendienteDeUbicar()
        {
            Assert.AreEqual("Recogidas de IF 3 uds. de 1 producto a ALG (traspaso 81344). 3 quedan pendientes de ubicar en ALG: ubíquelas con la PDA.",
                ReglasAlmacenesFicticios.Mensaje(SentidosTraspasoFicticio.RECOGER, "ALG", "IF", 3, 1, 81344, 3));
            StringAssert.Contains(ReglasAlmacenesFicticios.Mensaje(SentidosTraspasoFicticio.ENTREGAR, "REI", "IF", 1, 1, null, 0), "ensayo");
        }

        // ---------------------------------------------------------------- SQL

        [TestMethod]
        public void Sql_ElSaldoEsDeLasEmpresas1Y3_YLaConsultaNoLlevaCostesNiPrecios()
        {
            StringAssert.Contains(RepositorioAlmacenesFicticiosSql.SQL_EXISTENCIAS, "e.Empresa IN (@p0, @p1)");
            StringAssert.Contains(RepositorioAlmacenesFicticiosSql.SQL_EXISTENCIAS, "HAVING SUM(e.Cantidad) <> 0");
            foreach (string prohibido in new[] { "Coste", "PVP", "Precio", "Importe" })
            {
                Assert.IsFalse(RepositorioAlmacenesFicticiosSql.SQL_EXISTENCIAS.IndexOf(prohibido, StringComparison.OrdinalIgnoreCase) >= 0, prohibido);
            }
            StringAssert.Contains(RepositorioAlmacenesFicticiosSql.SQL_STOCK, "Empresa IN (@p0, @p1)");
            StringAssert.Contains(RepositorioAlmacenesFicticiosSql.SQL_STOCK, "{BLOQUEO}");
        }

        [TestMethod]
        public void Sql_LosHuecosLibresNoCuentanLoReservadoNiLoPendienteDeUbicar()
        {
            string sql = RepositorioAlmacenesFicticiosSql.SQL_HUECOS_LIBRES;
            StringAssert.Contains(sql, "Estado = 0");
            StringAssert.Contains(sql, "PedidoVta IS NULL");
            StringAssert.Contains(sql, "[NºOrdenRepo] IS NULL");
            StringAssert.Contains(sql, "Pasillo IS NOT NULL");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_LIBRES_DEL_HUECO_CON_ESPEJO, "WITH (UPDLOCK, HOLDLOCK)");
        }

        [TestMethod]
        public void Sql_LosDuenosSonLasFilasPropiasDelParametro_SinElDefecto()
        {
            StringAssert.Contains(RepositorioAlmacenesFicticiosSql.SQL_DUENOS, "f.Usuario <> '(defecto)'");
            StringAssert.Contains(RepositorioAlmacenesFicticiosSql.SQL_DUENOS, "v.Clave = 'Vendedor'");
            Assert.AreEqual("AlmacenFicticio", ReglasAlmacenesFicticios.CLAVE_PARAMETRO);
            Assert.AreEqual("_Ficticio", ReglasAlmacenesFicticios.DIARIO);
            Assert.AreEqual("TraspasoAlmacenFicticio", ReglasAlmacenesFicticios.TABLA_MODIFICACIONES);
            StringAssert.Contains(RepositorioAlmacenesFicticiosSql.SQL_ALMACENES, "AlmacénFicticio");
            StringAssert.Contains(RepositorioAlmacenesFicticiosSql.SQL_BLOQUEO, "sp_getapplock");
        }

        [TestMethod]
        public void Sql_ElNucleoNoEscribeEnExtractoProducto_SoloLoLee()
        {
            Assert.IsFalse(MovimientoTraspasoStockSql.SQL_APUNTE.IndexOf("INSERT", StringComparison.OrdinalIgnoreCase) >= 0);
            StringAssert.StartsWith(MovimientoTraspasoStockSql.SQL_APUNTE.Trim(), "SELECT");
        }

        // ---------------------------------------------------------------- el servicio

        private IRepositorioAlmacenesFicticios repositorio;
        private ITransaccionAlmacenFicticio transaccion;
        private IAvisadorAlmacenFicticio avisador;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioAlmacenesFicticios>();
            transaccion = A.Fake<ITransaccionAlmacenFicticio>();
            avisador = A.Fake<IAvisadorAlmacenFicticio>();
            A.CallTo(() => repositorio.LeerAlmacenes(A<string>._)).Returns(Task.FromResult(new List<AlmacenLeido>
            {
                new AlmacenLeido { Codigo = "ALG", Descripcion = "Algete", ControlUbicaciones = true },
                new AlmacenLeido { Codigo = "REI", Descripcion = "Reina" },
                new AlmacenLeido { Codigo = "IF", Descripcion = "Israel Fernández", EsFicticio = true },
                new AlmacenLeido { Codigo = "IM", Descripcion = "Irene", EsFicticio = true },
                new AlmacenLeido { Codigo = "PRV", Descripcion = "Proveedores", EsFicticio = true }
            }));
            A.CallTo(() => repositorio.LeerDuenos()).Returns(Task.FromResult(DUENOS.ToList()));
            A.CallTo(() => repositorio.LeerProductos(A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, ProductoTraspasoFicticio>(PRODUCTOS, StringComparer.OrdinalIgnoreCase)));
            A.CallTo(() => repositorio.LeerStock("ALG", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["45958"] = 20 }));
            A.CallTo(() => repositorio.LeerStock("REI", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["45958"] = 4 }));
            A.CallTo(() => repositorio.LeerStock("IF", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["45958"] = 2 }));
            A.CallTo(() => repositorio.LeerHuecosLibres("ALG", A<IEnumerable<string>>._)).Returns(Task.FromResult(HUECOS.ToList()));
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionAlmacenFicticio, Task<ResultadoTraspasoFicticioDTO>>>._))
                .ReturnsLazily((Func<ITransaccionAlmacenFicticio, Task<ResultadoTraspasoFicticioDTO>> trabajo) => trabajo(transaccion));
            A.CallTo(() => transaccion.SiguienteTraspaso()).Returns(Task.FromResult(81344));
            A.CallTo(() => transaccion.LeerPorClave(A<Guid>._)).Returns(Task.FromResult<ResultadoTraspasoFicticioDTO>(null));
            A.CallTo(() => transaccion.LeerStock("ALG", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["45958"] = 20 }));
            A.CallTo(() => transaccion.LeerStock("REI", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["45958"] = 4 }));
            A.CallTo(() => transaccion.LeerStock("IF", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["45958"] = 5 }));
            A.CallTo(() => transaccion.Contabilizar(A<int>._, A<IReadOnlyList<LineaMovimientoTraspaso>>._, A<string>._))
                .Returns(Task.FromResult(new List<int?> { 1, 2 }));
        }

        private ServicioAlmacenesFicticios Servicio() => new ServicioAlmacenesFicticios(repositorio, avisador);

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static IPrincipal ClienteTienda() =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", "15191") }, "prueba"));

        private static async Task<NestoBusinessException> Lanza(Func<Task> accion)
        {
            try
            {
                await accion();
            }
            catch (NestoBusinessException ex)
            {
                return ex;
            }
            Assert.Fail("Tenía que lanzar NestoBusinessException");
            return null;
        }

        [TestMethod]
        public async Task Entregar_DesdeAlgete_ContabilizaLaParejaSacaDelHuecoPropuestoYDejaRastro()
        {
            ResultadoTraspasoFicticioDTO r = await Servicio().Traspasar(Peticion(), false, Usuario("Andre", "Almacén"));

            Assert.AreEqual(81344, r.NumeroTraspaso);
            Assert.AreEqual("ALG", r.AlmacenPropio);
            Assert.AreEqual("003/001/001", r.Lineas[0].Hueco);
            Assert.IsTrue(r.Lineas[0].HuecoPropuesto);
            Assert.AreEqual(5, r.Saldos.Single().Unidades);
            A.CallTo(() => transaccion.Bloquear("1")).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => transaccion.SiguienteTraspaso()).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => transaccion.Contabilizar(81344, A<IReadOnlyList<LineaMovimientoTraspaso>>.That.Matches(l =>
                    l.Count == 2 && l[0].Almacen == "ALG" && l[0].Cantidad == -3 && l[1].Almacen == "IF" && l[1].Cantidad == 3), A<string>._))
                    .MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => transaccion.SacarDeHueco("ALG", "45958", A<HuecoUbicacion>.That.Matches(h => h.Codigo == "003001001"), 3, 81344, A<string>._))
                    .MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => transaccion.ApuntarRastro("Traspaso 81344", A<string>.That.Contains("\"NumeroTraspaso\":81344"), A<string>._))
                    .MustHaveHappenedOnceExactly());
            A.CallTo(() => transaccion.DejarPendienteDeUbicar(A<string>._, A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(A<IEnumerable<string>>.That.Matches(u => u.Single() == "Israel"), A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Entregar_Ensayo_NoEscribeNada_YDiceElSaldoQueQuedaria()
        {
            ResultadoTraspasoFicticioDTO r = await Servicio().Traspasar(Peticion(), true, Usuario("Andre", "Almacén"));

            Assert.IsTrue(r.Ensayo);
            Assert.IsNull(r.NumeroTraspaso);
            Assert.AreEqual(5, r.Saldos.Single().Unidades, "2 que tiene + 3");
            Assert.AreEqual("003/001/001", r.Lineas[0].Hueco);
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionAlmacenFicticio, Task<ResultadoTraspasoFicticioDTO>>>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(A<IEnumerable<string>>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Recoger_EnAlgete_LoDevueltoQuedaPendienteDeUbicar()
        {
            ResultadoTraspasoFicticioDTO r = await Servicio().Traspasar(Peticion("Recoger", new LineaTraspasoFicticioDTO { Producto = "45958", Cantidad = 2 }),
                false, Usuario("Andre", "Almacén"));

            Assert.AreEqual(2, r.UnidadesPendientesDeUbicar);
            StringAssert.Contains(r.Mensaje, "pendientes de ubicar");
            A.CallTo(() => transaccion.Contabilizar(81344, A<IReadOnlyList<LineaMovimientoTraspaso>>.That.Matches(l =>
                l[0].Almacen == "IF" && l[0].Cantidad == -2 && l[1].Almacen == "ALG" && l[1].Cantidad == 2), A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.DejarPendienteDeUbicar("ALG", "45958", 2, 81344, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.SacarDeHueco(A<string>._, A<string>._, A<HuecoUbicacion>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Recoger_MasDeLoQueTieneElVendedor_400SinTocarNada()
        {
            NestoBusinessException ex = await Lanza(() => Servicio().Traspasar(Peticion("Recoger", new LineaTraspasoFicticioDTO { Producto = "45958", Cantidad = 3 }),
                false, Usuario("Andre", "Almacén")));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            StringAssert.Contains(ex.Message, "En IF solo hay 2 de 45958");
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionAlmacenFicticio, Task<ResultadoTraspasoFicticioDTO>>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Entregar_SiElStockCambiaMientras_409YNoSeContabiliza()
        {
            A.CallTo(() => transaccion.LeerStock("ALG", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["45958"] = 1 }));

            NestoBusinessException ex = await Lanza(() => Servicio().Traspasar(Peticion(), false, Usuario("Andre", "Almacén")));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            A.CallTo(() => transaccion.Contabilizar(A<int>._, A<IReadOnlyList<LineaMovimientoTraspaso>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Entregar_DesdeLaTienda_ContraSuAlmacen_SinHuecos()
        {
            A.CallTo(() => repositorio.LeerParametroPropio("Paloma", "AlmacénPedidoVta")).Returns(Task.FromResult("REI"));

            ResultadoTraspasoFicticioDTO r = await Servicio().Traspasar(Peticion(), false, Usuario("Paloma", "Tiendas"));

            Assert.AreEqual("REI", r.AlmacenPropio);
            Assert.IsNull(r.Lineas[0].Hueco);
            A.CallTo(() => transaccion.Contabilizar(81344, A<IReadOnlyList<LineaMovimientoTraspaso>>.That.Matches(l => l[0].Almacen == "REI" && l[0].Cantidad == -3), A<string>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.LeerHuecosLibres(A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
            A.CallTo(() => transaccion.SacarDeHueco(A<string>._, A<string>._, A<HuecoUbicacion>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Entregar_LaTiendaSinStockSuficiente_400()
        {
            A.CallTo(() => repositorio.LeerParametroPropio("Paloma", "AlmacénPedidoVta")).Returns(Task.FromResult("REI"));
            NestoBusinessException ex = await Lanza(() => Servicio().Traspasar(
                Peticion("Entregar", new LineaTraspasoFicticioDTO { Producto = "45958", Cantidad = 5 }), false, Usuario("Paloma", "Tiendas")));
            StringAssert.Contains(ex.Message, "En REI solo hay 4");
        }

        [TestMethod]
        public async Task Traspasar_ElVendedorNoMueveNiElSuyo_403()
        {
            NestoBusinessException ex = await Lanza(() => Servicio().Traspasar(Peticion(), false, Usuario("Israel")));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task Traspasar_PrvDesdeLaTienda_403()
        {
            A.CallTo(() => repositorio.LeerParametroPropio("Paloma", "AlmacénPedidoVta")).Returns(Task.FromResult("REI"));
            TraspasoAlmacenFicticioDTO peticion = Peticion();
            peticion.AlmacenFicticio = "PRV";
            NestoBusinessException ex = await Lanza(() => Servicio().Traspasar(peticion, false, Usuario("Paloma", "Tiendas")));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task Traspasar_UnAlmacenQueNoEsFicticio_404()
        {
            TraspasoAlmacenFicticioDTO peticion = Peticion();
            peticion.AlmacenFicticio = "REI";
            NestoBusinessException ex = await Lanza(() => Servicio().Traspasar(peticion, false, Usuario("Andre", "Almacén")));
            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public async Task Traspasar_ConLaMismaClave_DevuelveElDeAntesSinContabilizar()
        {
            Guid clave = Guid.NewGuid();
            A.CallTo(() => transaccion.LeerPorClave(clave)).Returns(Task.FromResult(new ResultadoTraspasoFicticioDTO { NumeroTraspaso = 81000, Mensaje = "Entregadas." }));
            TraspasoAlmacenFicticioDTO peticion = Peticion();
            peticion.Clave = clave;

            ResultadoTraspasoFicticioDTO r = await Servicio().Traspasar(peticion, false, Usuario("Andre", "Almacén"));

            Assert.AreEqual(81000, r.NumeroTraspaso);
            StringAssert.StartsWith(r.Mensaje, "Este traspaso ya estaba hecho");
            A.CallTo(() => transaccion.Contabilizar(A<int>._, A<IReadOnlyList<LineaMovimientoTraspaso>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Traspasar_ConClave_LaClaveVaAlRastro()
        {
            Guid clave = Guid.NewGuid();
            TraspasoAlmacenFicticioDTO peticion = Peticion();
            peticion.Clave = clave;
            peticion.Comentario = "Para la feria";

            _ = await Servicio().Traspasar(peticion, false, Usuario("Andre", "Almacén"));

            A.CallTo(() => transaccion.ApuntarRastro(clave.ToString("D"), A<string>.That.Contains("Para la feria"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Listar_ElVendedorSoloVeElSuyo_YAlmacenVeTodos()
        {
            List<AlmacenFicticioDTO> deIsrael = await Servicio().Listar(Usuario("Israel"));
            Assert.AreEqual("IF", deIsrael.Single().Codigo);
            Assert.IsTrue(deIsrael.Single().EsSuyo);
            Assert.IsFalse(deIsrael.Single().PuedeTraspasar);
            CollectionAssert.AreEqual(new[] { "IF" }, deIsrael.Single().Vendedores);

            List<AlmacenFicticioDTO> deAlmacen = await Servicio().Listar(Usuario("Andre", "Almacén"));
            CollectionAssert.AreEquivalent(new[] { "IF", "IM", "PRV" }, deAlmacen.Select(a => a.Codigo).ToList());
            Assert.IsTrue(deAlmacen.All(a => a.PuedeTraspasar && a.AlmacenContra == "ALG"));
        }

        [TestMethod]
        public async Task Listar_ElJefeDeEquipoVeLosDeSuEquipo()
        {
            A.CallTo(() => repositorio.VendedorDelUsuario(A<IPrincipal>._)).Returns("ASH");
            A.CallTo(() => repositorio.VendedoresDelEquipo("1", "ASH")).Returns(Task.FromResult(new List<string> { "ASH", "IF" }));

            List<AlmacenFicticioDTO> lista = await Servicio().Listar(Usuario("Sancho"));

            // AB (el suyo) no está entre los almacenes activos de esta prueba; IF es de su equipo e IM no
            CollectionAssert.AreEquivalent(new[] { "IF" }, lista.Select(a => a.Codigo).ToList());
            Assert.IsFalse(lista.Single().EsSuyo);
        }

        [TestMethod]
        public async Task ElClienteDeLaTienda_403EnTodo()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Lanza(() => Servicio().Listar(ClienteTienda()))).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Lanza(() => Servicio().Existencias("IF", ClienteTienda()))).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Lanza(() => Servicio().Traspasar(Peticion(), true, ClienteTienda()))).StatusCode);
        }

        [TestMethod]
        public async Task Existencias_DeOtroVendedor_403_YDelSuyo_Las_Devuelve()
        {
            A.CallTo(() => repositorio.LeerExistencias("IF")).Returns(Task.FromResult(new List<ExistenciaAlmacenFicticioDTO>
            {
                new ExistenciaAlmacenFicticioDTO { Producto = "45958", Nombre = "CREMA", Unidades = 3 },
                new ExistenciaAlmacenFicticioDTO { Producto = "17404", Nombre = "OTRA", Unidades = 2 }
            }));

            ExistenciasAlmacenFicticioDTO suyas = await Servicio().Existencias("if", Usuario("Israel"));
            Assert.AreEqual("IF", suyas.Almacen);
            Assert.AreEqual(5, suyas.TotalUnidades);
            Assert.AreEqual(2, suyas.NumeroProductos);

            Assert.AreEqual(HttpStatusCode.Forbidden, (await Lanza(() => Servicio().Existencias("IM", Usuario("Israel")))).StatusCode);
        }

        [TestMethod]
        public async Task Controlador_DevuelveLoQueDiceElServicio()
        {
            IServicioAlmacenesFicticios servicio = A.Fake<IServicioAlmacenesFicticios>();
            A.CallTo(() => servicio.Traspasar(A<TraspasoAlmacenFicticioDTO>._, true, A<IPrincipal>._))
                .Returns(Task.FromResult(new ResultadoTraspasoFicticioDTO { Ensayo = true }));
            A.CallTo(() => servicio.Listar(A<IPrincipal>._)).Returns(Task.FromResult(new List<AlmacenFicticioDTO> { new AlmacenFicticioDTO { Codigo = "IF" } }));
            var controlador = new AlmacenesFicticiosController(null, servicio);

            var traspaso = await controlador.PostTraspaso(Peticion(), true) as OkNegotiatedContentResult<ResultadoTraspasoFicticioDTO>;
            Assert.IsTrue(traspaso.Content.Ensayo);
            var lista = await controlador.GetAlmacenesFicticios() as OkNegotiatedContentResult<List<AlmacenFicticioDTO>>;
            Assert.AreEqual("IF", lista.Content.Single().Codigo);
        }

        [TestMethod]
        public void Notificacion_DiceQueSeHaEntregadoYElTraspaso()
        {
            NotificacionPushDTO n = ReglasAlmacenesFicticios.Notificacion(new ResultadoTraspasoFicticioDTO
            {
                NumeroTraspaso = 81344, AlmacenFicticio = "IF", AlmacenPropio = "ALC", Sentido = SentidosTraspasoFicticio.ENTREGAR,
                Lineas = new List<LineaTraspasoFicticioHechaDTO> { new LineaTraspasoFicticioHechaDTO { Producto = "45958", Nombre = "CREMA", Cantidad = 3 } }
            }, "Paloma");
            Assert.AreEqual("Le han entregado material en IF", n.Titulo);
            Assert.AreEqual("Paloma (ALC) le ha entregado: 45958 CREMA (3). Traspaso 81344.", n.Cuerpo);
            Assert.AreEqual("TraspasoAlmacenFicticio", n.Tipo);
        }
    }
}
