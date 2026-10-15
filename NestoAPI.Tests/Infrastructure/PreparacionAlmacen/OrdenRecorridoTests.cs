using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#641 (corte 3 de #639): el orden de recorrido por calles. Una calle junta pasillos que se recorren a la vez
    /// (las dos paredes del mismo pasillo físico), con su orden y su sentido; por cada columna, primero lo que se coge a
    /// mano (fila >= FilaAMano) de todos sus pasillos y después, con la escalera, las filas de arriba. Sin calles, el orden
    /// de siempre (pasillo, columna, fila).
    /// </summary>
    [TestClass]
    public class OrdenRecorridoTests
    {
        private static PasilloRecorrido P(string pasillo, int idCalle, string calle, int orden, string sentido, int posicion, string nombre = null)
        {
            return new PasilloRecorrido { Pasillo = pasillo, IdCalle = idCalle, Calle = calle, OrdenCalle = orden, Sentido = sentido, Posicion = posicion, Nombre = nombre };
        }

        /// <summary>La semilla de Scripts/Issue641_RecorridoCalles.sql (Carlos, 15/10/26).</summary>
        private static OrdenRecorrido Algete(int? filaAMano = null)
        {
            return new OrdenRecorrido(new[]
            {
                P("001", 1, "a", 10, "A", 1), P("002", 1, "a", 10, "A", 2),
                P("011", 2, "b", 20, "A", 1),
                P("012", 3, "c", 30, "A", 1),
                P("003", 4, "d", 40, "D", 1), P("004", 4, "d", 40, "D", 2),
                P("005", 5, "e", 50, "A", 1),
                P("007", 6, "f", 60, "A", 1), P("013", 6, "f", 60, "A", 2),
                P("010", 7, "g", 70, "A", 1),
                P("009", 8, "h", 80, "D", 1), P("008", 8, "h", 80, "D", 2),
                P("006", 9, "p6", 90, "A", 1)
            }, filaAMano);
        }

        private static LineaPickingAlmacenDTO L(string producto, string pasillo, string fila, string columna)
        {
            return new LineaPickingAlmacenDTO { Producto = producto, Pasillo = pasillo, Fila = fila, Columna = columna, CodigoBarras = "84" + producto, Cantidad = 1 };
        }

        private static List<string> Productos(IEnumerable<LineaPickingAlmacenDTO> lineas) => lineas.Select(l => l.Producto).ToList();

        /// <summary>
        /// Las paradas del picking 99829 de Algete (14/10/26), tal como las da Ubicaciones: producto, pasillo, fila, columna.
        /// Sin hueco = null (van al final).
        /// </summary>
        private static List<LineaPickingAlmacenDTO> Picking99829()
        {
            string[] filas =
            {
                "16627|021|000|000", "22268|007|003|005", "23126|007|003|002", "24365|001|002|008", "29786|||", "29787|013|000|004",
                "33896|001|004|011", "34058|009|002|006", "37816|001|003|013", "37817|001|001|003", "38709|||", "39421|001|008|014",
                "39424|001|008|013", "39523|001|003|003", "39634|036|000|000", "39798|002|003|001", "39825|033|000|001", "40309|001|004|004",
                "40521|005|003|004", "40595|009|003|009", "40627|||", "41326|033|000|005", "41687|033|000|005", "41941|002|007|013",
                "42276|002|007|002", "42277|002|007|003", "42391|036|000|000", "42860|||", "43399|001|009|002", "43541|001|006|013",
                "43543|001|004|007", "43572|033|000|004", "43637|001|001|011", "43759|001|003|008", "43887|001|002|004", "43888|001|001|011",
                "44049|003|003|013", "44206|033|000|001", "44494|033|000|001", "44703|||", "45489|002|002|002", "45910|001|001|004",
                "45912|||", "45915|011|004|002", "45924|011|004|002", "45931|007|003|002", "46021|||", "46066|||"
            };
            return filas.Select(f => f.Split('|'))
                .Select(p => L(p[0], p[1] == "" ? null : p[1], p[2] == "" ? null : p[2], p[3] == "" ? null : p[3]))
                .ToList();
        }

        /// <summary>El orden de antes del corte 3, copiado tal cual de CasadorEscaneos.OrdenarRecorrido.</summary>
        private static List<string> OrdenDeAntes(IEnumerable<LineaPickingAlmacenDTO> lineas)
        {
            return lineas
                .OrderBy(l => string.IsNullOrWhiteSpace(l.Pasillo))
                .ThenBy(l => l.Pasillo?.Trim(), StringComparer.Ordinal)
                .ThenBy(l => l.Columna?.Trim(), StringComparer.Ordinal)
                .ThenBy(l => l.Fila?.Trim(), StringComparer.Ordinal)
                .ThenBy(l => l.Producto?.Trim(), StringComparer.Ordinal)
                .Select(l => l.Producto)
                .ToList();
        }

        #region Sin filas en las tablas: idéntico a hoy

        [TestMethod]
        public void SinCalles_UnPickingReal_ElOrdenEsIdenticoAlDeAntes()
        {
            List<string> antes = OrdenDeAntes(Picking99829());

            CollectionAssert.AreEqual(antes, Productos(CasadorEscaneos.OrdenarRecorrido(Picking99829())), "Sin decir nada");
            CollectionAssert.AreEqual(antes, Productos(CasadorEscaneos.OrdenarRecorrido(Picking99829(), OrdenRecorrido.ComoHoy)), "ComoHoy");
            CollectionAssert.AreEqual(antes, Productos(CasadorEscaneos.OrdenarRecorrido(Picking99829(), new OrdenRecorrido(new PasilloRecorrido[0]))), "Tablas vacías");
            CollectionAssert.AreEqual(antes, Productos(CasadorEscaneos.OrdenarRecorrido(Picking99829(), null)), "Sin lector");
        }

        [TestMethod]
        public void SinCalles_PasillosSoloConNombre_NoCambianElOrden()
        {
            var soloNombres = new OrdenRecorrido(new[]
            {
                new PasilloRecorrido { Pasillo = "005", Nombre = "Esmaltes" },
                new PasilloRecorrido { Pasillo = "001", Nombre = "Limas" }
            });

            CollectionAssert.AreEqual(OrdenDeAntes(Picking99829()), Productos(CasadorEscaneos.OrdenarRecorrido(Picking99829(), soloNombres)));
            Assert.IsFalse(soloNombres.TieneCalles);
            Assert.AreEqual("Esmaltes", soloNombres.NombrePasillo("005"));
            Assert.AreEqual("Esmaltes", soloNombres.NombrePasillo(" 5 "), "El pasillo se normaliza a tres cifras");
            Assert.IsNull(soloNombres.NombrePasillo("002"));
        }

        [TestMethod]
        public void ConLasCallesDeAlgete_UnPickingReal_EnsenaLasPrimerasParadas()
        {
            // No comprueba nada nuevo: deja en la salida de la prueba cómo queda un picking real con y sin calles
            List<LineaPickingAlmacenDTO> sin = CasadorEscaneos.OrdenarRecorrido(Picking99829());
            List<LineaPickingAlmacenDTO> con = CasadorEscaneos.OrdenarRecorrido(Picking99829(), Algete());
            for (int i = 0; i < 25; i++)
            {
                Console.WriteLine($"{i + 1,2}  hoy {sin[i].Producto} {sin[i].Ubicacion,-12}  con calles {con[i].Producto} {con[i].Ubicacion}");
            }
            Assert.AreEqual(sin.Count, con.Count);
            CollectionAssert.AreEquivalent(Productos(sin), Productos(con));
        }

        #endregion

        #region Calles

        [TestMethod]
        public void Calle1y2_PorCadaColumna_PrimeroAManoDeLosDosPasillosYLuegoLaEscaleraDeLosDos()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("2-ESC-C2", "002", "003", "002"),
                L("1-ESC-C1", "001", "002", "001"),
                L("2-MANO-C1", "002", "004", "001"),
                L("1-MANO-C2", "001", "006", "002"),
                L("2-ESC-C1", "002", "001", "001"),
                L("1-MANO-C1", "001", "005", "001")
            }, Algete());

            CollectionAssert.AreEqual(new[] { "1-MANO-C1", "2-MANO-C1", "1-ESC-C1", "2-ESC-C1", "1-MANO-C2", "2-ESC-C2" }, Productos(recorrido));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, recorrido.Select(l => l.Orden).ToList());
        }

        [TestMethod]
        public void DentroDeCadaNivel_AManoDeArribaAbajo_ConLaEscaleraSubiendo()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("F1", "001", "001", "003"), L("F7", "001", "007", "003"), L("F3", "001", "003", "003"),
                L("F4", "001", "004", "003"), L("F2", "001", "002", "003"), L("F5", "001", "005", "003")
            }, Algete());

            CollectionAssert.AreEqual(new[] { "F4", "F5", "F7", "F3", "F2", "F1" }, Productos(recorrido));
        }

        [TestMethod]
        public void Calle3y4_Descendente_EmpiezaPorLaUltimaColumna()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("4-C001", "004", "005", "001"),
                L("3-C014", "003", "005", "014"),
                L("4-C016", "004", "005", "016"),
                L("3-C001", "003", "005", "001"),
                L("4-C014", "004", "006", "014")
            }, Algete());

            CollectionAssert.AreEqual(new[] { "4-C016", "3-C014", "4-C014", "3-C001", "4-C001" }, Productos(recorrido));
        }

        [TestMethod]
        public void Calle9y8_Descendente_PrimeroEl9EnCadaColumna()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("8-C005-MANO", "008", "004", "005"),
                L("9-C005-ESC", "009", "002", "005"),
                L("8-C005-ESC", "008", "001", "005"),
                L("9-C014", "009", "003", "014"),
                L("9-C005-MANO", "009", "004", "005"),
                L("8-C001", "008", "002", "001")
            }, Algete());

            CollectionAssert.AreEqual(new[] { "9-C014", "9-C005-MANO", "8-C005-MANO", "9-C005-ESC", "8-C005-ESC", "8-C001" }, Productos(recorrido));
        }

        [TestMethod]
        public void CalleDeUnSoloPasillo_TambienVaPrimeroAManoYLuegoEscalera()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("C1-ESC", "005", "002", "001"),
                L("C2-MANO", "005", "008", "002"),
                L("C1-MANO", "005", "006", "001")
            }, Algete());

            CollectionAssert.AreEqual(new[] { "C1-MANO", "C1-ESC", "C2-MANO" }, Productos(recorrido));
        }

        [TestMethod]
        public void LasCalles_VanEnSuOrden_YElPasillo6DespuesDeLaH()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("P020", "020", "000", "001"), L("P006", "006", "001", "001"), L("P008", "008", "004", "001"),
                L("P010", "010", "004", "001"), L("P007", "007", "004", "001"), L("P005", "005", "004", "001"),
                L("P003", "003", "004", "001"), L("P012", "012", "000", "001"), L("P011", "011", "004", "001"),
                L("P001", "001", "004", "001")
            }, Algete());

            CollectionAssert.AreEqual(new[] { "P001", "P011", "P012", "P003", "P005", "P007", "P010", "P008", "P006", "P020" }, Productos(recorrido));
        }

        [TestMethod]
        public void PasilloSinCalle_AlFinal_ComoHoySinNiveles()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("099", "099", "001", "001"), L("000", "000", "000", "000"), L("SIN", null, null, null),
                L("020-F5", "020", "005", "001"), L("020-F1", "020", "001", "001"), L("006", "006", "002", "004")
            }, Algete());

            // 006 tiene calle (la última); 000, 020 y 099 no: pasillo, columna y fila ascendentes (la 001 antes que la 005)
            CollectionAssert.AreEqual(new[] { "006", "000", "020-F1", "020-F5", "099", "SIN" }, Productos(recorrido));
        }

        [TestMethod]
        public void ColumnaNueva_EnUnPasilloConCalle_HeredaSuSitio()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("B", "011", "004", "001"), L("NUEVA", "002", "004", "017"), L("VIEJA", "001", "004", "016")
            }, Algete());

            CollectionAssert.AreEqual(new[] { "VIEJA", "NUEVA", "B" }, Productos(recorrido));
        }

        [TestMethod]
        public void FilaAMano_EsUnParametroDelAlmacen()
        {
            LineaPickingAlmacenDTO[] lineas = { L("F1", "001", "001", "001"), L("F3", "001", "003", "001"), L("F2", "001", "002", "001") };

            CollectionAssert.AreEqual(new[] { "F3", "F2", "F1" }, Productos(CasadorEscaneos.OrdenarRecorrido(lineas, Algete())), "Con 4, la 3 es de escalera");
            CollectionAssert.AreEqual(new[] { "F3", "F2", "F1" }, Productos(CasadorEscaneos.OrdenarRecorrido(lineas, Algete(4))));
            CollectionAssert.AreEqual(new[] { "F1", "F2", "F3" }, Productos(CasadorEscaneos.OrdenarRecorrido(lineas, Algete(1))), "Con 1, todo a mano");
            Assert.IsTrue(Algete().EsDeEscalera("003"));
            Assert.IsFalse(Algete(3).EsDeEscalera("003"));
            Assert.IsTrue(Algete(3).EsDeEscalera("002"));
            Assert.AreEqual(OrdenRecorrido.FILA_A_MANO_POR_DEFECTO, Algete(0).FilaAMano, "Un 0 o un negativo es el valor por defecto");
        }

        [TestMethod]
        public void FilaAMano3_LaFila3SeCogeAManoAntesQueLaEscalera()
        {
            LineaPickingAlmacenDTO[] lineas = { L("F1", "001", "001", "001"), L("F3", "002", "003", "001"), L("F2", "001", "002", "001") };

            CollectionAssert.AreEqual(new[] { "F2", "F1", "F3" }, Productos(CasadorEscaneos.OrdenarRecorrido(lineas, Algete())), "Con 4: escalera del 1 y luego del 2");
            CollectionAssert.AreEqual(new[] { "F3", "F2", "F1" }, Productos(CasadorEscaneos.OrdenarRecorrido(lineas, Algete(3))), "Con 3: la 3 del 2 es a mano");
        }

        [TestMethod]
        public void LaFila000_SueloOPale_CuentaComoAMano()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("ESC", "013", "001", "004"), L("SUELO", "013", "000", "004"), L("MANO-7", "007", "005", "004")
            }, Algete());

            // Calle f (7 y 13), columna 004: a mano el 7 y el suelo del 13; luego la escalera
            CollectionAssert.AreEqual(new[] { "MANO-7", "SUELO", "ESC" }, Productos(recorrido));
        }

        [TestMethod]
        public void UnPasilloEscritoSinCeros_EsElMismo()
        {
            Assert.AreEqual("004", OrdenRecorrido.NormalizarPasillo(" 4"));
            Assert.AreEqual("004", OrdenRecorrido.NormalizarPasillo("004 "));
            Assert.IsNull(OrdenRecorrido.NormalizarPasillo("  "));
            Assert.AreEqual(0, Algete().Clave("1", "001", "005").CompareTo(Algete().Clave("001", "001", "005")));
        }

        #endregion

        #region Los otros sitios que ordenan por hueco usan la misma regla

        private static ILectorOrdenRecorrido LectorDe(OrdenRecorrido orden, string almacen = "ALG")
        {
            ILectorOrdenRecorrido lector = A.Fake<ILectorOrdenRecorrido>();
            A.CallTo(() => lector.DelAlmacen(A<string>._, A<string>._)).Returns(OrdenRecorrido.ComoHoy);
            A.CallTo(() => lector.DelAlmacen("1", almacen)).Returns(orden);
            return lector;
        }

        [TestMethod]
        public async Task Salidas_ElPickingSeOrdenaConLasCallesDeSuAlmacen()
        {
            IRepositorioPreparacionAlmacen repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerLineasPicking("1", 99829)).Returns(new List<LineaPickingAlmacenDTO>
            {
                L("P003", "003", "005", "001"), L("P002", "002", "005", "001"), L("P011", "011", "005", "001")
            });
            A.CallTo(() => repositorio.LeerLecturasDelPicking("1", 99829)).Returns(new List<LecturaPickingAlmacen>());
            A.CallTo(() => repositorio.LeerAlmacenPicking("1", 99829)).Returns("ALG");
            var servicio = new ServicioSalidas(new IOrigenSalida[] { new OrigenSalidaPicking(repositorio), new OrigenSalidaReposicion(repositorio) },
                ordenRecorrido: LectorDe(Algete()));

            RecogidaAlmacenDTO recogida = await servicio.LeerRecogida("1", "PICK", 99829);

            CollectionAssert.AreEqual(new[] { "P002", "P011", "P003" }, recogida.Lineas.Select(l => l.Producto).ToList());
        }

        [TestMethod]
        public async Task Salidas_LaReposicionSeOrdenaConLasCallesDelOrigen()
        {
            IRepositorioPreparacionAlmacen repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerReposicionSalida("1", 80905)).Returns(new ReposicionSalida
            {
                Destino = "REI",
                Almacen = "ALG",
                Lineas = new List<LineaPickingAlmacenDTO> { L("P003", "003", "005", "001"), L("P002", "002", "005", "001"), L("P011", "011", "005", "001") }
            });
            A.CallTo(() => repositorio.LeerLecturasDeSalida("1", "REPO", 80905)).Returns(new List<LecturaPickingAlmacen>());
            var servicio = new ServicioSalidas(new IOrigenSalida[] { new OrigenSalidaPicking(repositorio), new OrigenSalidaReposicion(repositorio) },
                ordenRecorrido: LectorDe(Algete()));

            RecogidaAlmacenDTO recogida = await servicio.LeerRecogida("1", "REPO", 80905);

            CollectionAssert.AreEqual(new[] { "P002", "P011", "P003" }, recogida.Lineas.Select(l => l.Producto).ToList());
        }

        [TestMethod]
        public async Task Salidas_SinAlmacen_ElOrdenDeSiempre()
        {
            IRepositorioPreparacionAlmacen repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerLineasPicking("1", 99829)).Returns(new List<LineaPickingAlmacenDTO>
            {
                L("P003", "003", "005", "001"), L("P002", "002", "005", "001"), L("P011", "011", "005", "001")
            });
            A.CallTo(() => repositorio.LeerLecturasDelPicking("1", 99829)).Returns(new List<LecturaPickingAlmacen>());
            A.CallTo(() => repositorio.LeerAlmacenPicking("1", 99829)).Returns((string)null);
            ILectorOrdenRecorrido lector = LectorDe(Algete());
            var servicio = new ServicioSalidas(new IOrigenSalida[] { new OrigenSalidaPicking(repositorio) }, ordenRecorrido: lector);

            RecogidaAlmacenDTO recogida = await servicio.LeerRecogida("1", "PICK", 99829);

            CollectionAssert.AreEqual(new[] { "P002", "P003", "P011" }, recogida.Lineas.Select(l => l.Producto).ToList());
            A.CallTo(() => lector.DelAlmacen(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ubicaciones_LosHuecosDeUnProducto_EnElOrdenDelRecorrido()
        {
            IRepositorioUbicacionesAlmacen repositorio = A.Fake<IRepositorioUbicacionesAlmacen>();
            A.CallTo(() => repositorio.BuscarProductos("1", "29712")).Returns(new List<FilaProductoAlmacen> { new FilaProductoAlmacen { Producto = "29712" } });
            A.CallTo(() => repositorio.LeerUbicacionesDelProducto("1", "ALG", "29712")).Returns(new List<FilaUbicacionProducto>
            {
                new FilaUbicacionProducto { Producto = "29712", Pasillo = "003", Fila = "005", Columna = "001", Cantidad = 2 },
                new FilaUbicacionProducto { Producto = "29712", Pasillo = "002", Fila = "005", Columna = "001", Cantidad = 2 },
                new FilaUbicacionProducto { Producto = "29712", Pasillo = "011", Fila = "005", Columna = "001", Cantidad = 2 }
            });

            List<ProductoAlmacenDTO> sinCalles = await new ServicioUbicacionesAlmacen(repositorio).BuscarProducto("1", "ALG", "29712");
            List<ProductoAlmacenDTO> conCalles = await new ServicioUbicacionesAlmacen(repositorio, LectorDe(Algete())).BuscarProducto("1", "ALG", "29712");

            CollectionAssert.AreEqual(new[] { "002", "003", "011" }, sinCalles.Single().Ubicaciones.Select(u => u.Pasillo).ToList());
            CollectionAssert.AreEqual(new[] { "002", "011", "003" }, conCalles.Single().Ubicaciones.Select(u => u.Pasillo).ToList());
        }

        [TestMethod]
        public void Ubicaciones_LoPendienteDeUbicar_SeRecorrePorLasCalles()
        {
            var pendiente = new[]
            {
                new FilaPendienteDeUbicar { Producto = "A", Cantidad = 1 },
                new FilaPendienteDeUbicar { Producto = "B", Cantidad = 1 },
                new FilaPendienteDeUbicar { Producto = "C", Cantidad = 1 }
            };
            var huecos = new[]
            {
                new FilaUbicacionProducto { Producto = "A", Pasillo = "003", Fila = "005", Columna = "001", Cantidad = 1 },
                new FilaUbicacionProducto { Producto = "B", Pasillo = "011", Fila = "005", Columna = "001", Cantidad = 1 },
                new FilaUbicacionProducto { Producto = "C", Pasillo = "002", Fila = "005", Columna = "001", Cantidad = 1 }
            };

            CollectionAssert.AreEqual(new[] { "C", "A", "B" }, ServicioUbicacionesAlmacen.MontarPendiente(pendiente, huecos).Select(p => p.Producto).ToList());
            CollectionAssert.AreEqual(new[] { "C", "B", "A" }, ServicioUbicacionesAlmacen.MontarPendiente(pendiente, huecos, Algete()).Select(p => p.Producto).ToList());
        }

        private static HuecoAlternativoDTO Alternativa(string pasillo, string fila, string columna)
        {
            return new HuecoAlternativoDTO { Ubicacion = $"{pasillo}/{fila}/{columna}", Codigo = pasillo + fila + columna, Cantidad = 1 };
        }

        [TestMethod]
        public void CambioHueco_SinCalles_ComoElOrderByDeAntes()
        {
            // Antes: ORDER BY u.Pasillo, u.Columna, u.Fila en el SQL
            List<HuecoAlternativoDTO> alternativas = ServicioCambioHuecoPicking.OrdenarAlternativas(new[]
            {
                Alternativa("004", "001", "002"), Alternativa("001", "005", "003"), Alternativa("001", "002", "003"), Alternativa("001", "009", "001")
            }, OrdenRecorrido.ComoHoy);

            CollectionAssert.AreEqual(new[] { "001009001", "001002003", "001005003", "004001002" }, alternativas.Select(a => a.Codigo).ToList());
        }

        [TestMethod]
        public async Task CambioHueco_ConCalles_LasAlternativasEnElOrdenDelRecorrido()
        {
            IRepositorioCambioHueco repositorio = A.Fake<IRepositorioCambioHueco>();
            A.CallTo(() => repositorio.LeerAlternativas("1", 99829, "22624", "001007002")).Returns(new List<HuecoAlternativoDTO>
            {
                Alternativa("003", "005", "001"), Alternativa("002", "005", "001"), Alternativa("011", "005", "001")
            });
            A.CallTo(() => repositorio.LeerAlmacenPicking("1", 99829)).Returns("ALG");

            List<HuecoAlternativoDTO> alternativas = await new ServicioCambioHuecoPicking(repositorio, LectorDe(Algete()))
                .LeerAlternativas("1", 99829, "22624", "001/007/002");

            CollectionAssert.AreEqual(new[] { "002005001", "011005001", "003005001" }, alternativas.Select(a => a.Codigo).ToList());
        }

        #endregion
    }
}
