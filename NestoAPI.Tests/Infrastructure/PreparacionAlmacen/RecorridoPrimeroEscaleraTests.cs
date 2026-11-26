using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#888 (sugerencia 1418, Santiago; Carlos: sí, configurable por almacén): dentro de cada columna, primero la escalera (filas de
    /// arriba, de arriba abajo) y después lo que se coge a mano. Sin el dato (Almacenes.PrimeroEscalera), el orden de siempre.
    /// </summary>
    [TestClass]
    public class RecorridoPrimeroEscaleraTests
    {
        private static PasilloRecorrido P(string pasillo, int posicion) =>
            new PasilloRecorrido { Pasillo = pasillo, IdCalle = 1, Calle = "a", OrdenCalle = 10, Sentido = "A", Posicion = posicion };

        private static OrdenRecorrido Calle(bool primeroEscalera) => new OrdenRecorrido(new[] { P("001", 1), P("002", 2) }, 4, primeroEscalera);

        private static LineaPickingAlmacenDTO L(string producto, string pasillo, string fila, string columna) =>
            new LineaPickingAlmacenDTO { Producto = producto, Pasillo = pasillo, Fila = fila, Columna = columna, CodigoBarras = "84" + producto, Cantidad = 1 };

        private static LineaPickingAlmacenDTO[] Lineas() => new[]
        {
            L("1-005", "001", "005", "001"), L("1-001", "001", "001", "001"), L("2-004", "002", "004", "001"), L("1-003", "001", "003", "001"),
            L("2-002", "002", "002", "001"), L("1-004", "001", "004", "001"), L("1-002", "001", "002", "001"), L("SIG-COL", "001", "004", "002"),
            L("SIN-CALLE", "030", "001", "001")
        };

        private static List<string> Orden(OrdenRecorrido orden) =>
            CasadorEscaneos.OrdenarRecorrido(Lineas(), orden).Select(l => l.Producto).ToList();

        [TestMethod]
        public void SinElDato_ElOrdenDeSiempre_AManoYLuegoLaEscaleraSubiendo()
        {
            CollectionAssert.AreEqual(new[] { "1-004", "1-005", "2-004", "1-003", "1-002", "1-001", "2-002", "SIG-COL", "SIN-CALLE" }, Orden(Calle(false)));
            CollectionAssert.AreEqual(Orden(new OrdenRecorrido(new[] { P("001", 1), P("002", 2) }, 4)), Orden(Calle(false)), "Sin decir nada = false");
            Assert.IsFalse(OrdenRecorrido.ComoHoy.PrimeroEscalera);
        }

        [TestMethod]
        public void PrimeroEscalera_EnCadaColumnaLaEscaleraDeArribaAbajo_YLuegoAMano()
        {
            CollectionAssert.AreEqual(new[] { "1-001", "1-002", "1-003", "2-002", "1-004", "1-005", "2-004", "SIG-COL", "SIN-CALLE" }, Orden(Calle(true)));
        }

        [TestMethod]
        public void PrimeroEscalera_NoTocaLosPasillosSinCalle()
        {
            var sinCalles = new OrdenRecorrido(null, 4, true);
            LineaPickingAlmacenDTO[] lineas = { L("B", "001", "004", "001"), L("A", "001", "001", "001") };

            CollectionAssert.AreEqual(new[] { "A", "B" }, CasadorEscaneos.OrdenarRecorrido(lineas, sinCalles).Select(l => l.Producto).ToList());
        }

        [TestMethod]
        public void VistaDelMantenimiento_TramosConLaEscaleraPrimero()
        {
            var calles = new List<CalleRecorridoGuardada> { new CalleRecorridoGuardada { Id = 1, Calle = "a", Orden = 10, Sentido = "A" } };
            var pasillos = new List<PasilloRecorrido> { new PasilloRecorrido { Pasillo = "001", IdCalle = 1, Posicion = 1 } };
            var huecos = new List<HuecoRecorrido>
            {
                new HuecoRecorrido { Pasillo = "001", Columna = "001", Fila = "004" },
                new HuecoRecorrido { Pasillo = "001", Columna = "001", Fila = "001" },
                new HuecoRecorrido { Pasillo = "001", Columna = "001", Fila = "002" }
            };

            RecorridoAlmacenDTO vista = ReglasMantenimientoRecorrido.ConstruirVista("1", "ALG", true, calles, pasillos, 4, huecos, true);

            Assert.IsTrue(vista.PrimeroEscalera);
            CollectionAssert.AreEqual(new[] { "Escalera", "AMano" }, vista.Tramos.Select(t => t.Nivel).ToArray());
            CollectionAssert.AreEqual(new[] { "001", "002" }, vista.Tramos[0].Filas.ToArray());
        }

        [TestMethod]
        public async Task Leer_DevuelveElDatoGuardado()
        {
            var repositorio = A.Fake<IRepositorioRecorrido>();
            A.CallTo(() => repositorio.TablasCreadas()).Returns(true);
            A.CallTo(() => repositorio.LeerPrimeroEscalera("1", "ALG")).Returns(true);

            RecorridoAlmacenDTO vista = await new ServicioRecorridoAlmacen(repositorio).Leer("1", "ALG");

            Assert.IsTrue(vista.PrimeroEscalera);
        }

        [TestMethod]
        public async Task Guardar_ConElDato_LoGuarda_YNullNoLoToca()
        {
            var repositorio = A.Fake<IRepositorioRecorrido>();
            A.CallTo(() => repositorio.TablasCreadas()).Returns(true);
            A.CallTo(() => repositorio.ColumnaPrimeroEscalera()).Returns(true);
            var guardados = new List<RecorridoNormalizado>();
            A.CallTo(() => repositorio.Guardar("1", "ALG", A<RecorridoNormalizado>._, "Santiago"))
                .Invokes((string e, string a, RecorridoNormalizado r, string u) => guardados.Add(r));

            _ = await new ServicioRecorridoAlmacen(repositorio).Guardar("1", "ALG", new PeticionRecorridoAlmacenDTO { PrimeroEscalera = true }, "Santiago", false);
            _ = await new ServicioRecorridoAlmacen(repositorio).Guardar("1", "ALG", new PeticionRecorridoAlmacenDTO(), "Santiago", false);

            Assert.AreEqual(true, guardados[0].PrimeroEscalera);
            Assert.IsNull(guardados[1].PrimeroEscalera);
        }

        [TestMethod]
        public async Task Guardar_SinLaColumna_PedirloDa503_YApagarloNo()
        {
            var repositorio = A.Fake<IRepositorioRecorrido>();
            A.CallTo(() => repositorio.TablasCreadas()).Returns(true);
            A.CallTo(() => repositorio.ColumnaPrimeroEscalera()).Returns(false);

            await Assert.ThrowsExceptionAsync<RecorridoSinColumnaPrimeroEscaleraException>(() =>
                new ServicioRecorridoAlmacen(repositorio).Guardar("1", "ALG", new PeticionRecorridoAlmacenDTO { PrimeroEscalera = true }, "Santiago", false));
            _ = await new ServicioRecorridoAlmacen(repositorio).Guardar("1", "ALG", new PeticionRecorridoAlmacenDTO { PrimeroEscalera = false }, "Santiago", false);
            A.CallTo(() => repositorio.Guardar("1", "ALG", A<RecorridoNormalizado>.That.Matches(r => r.PrimeroEscalera == false), "Santiago")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Ensayo_EnseñaElOrdenConLoPedido()
        {
            var repositorio = A.Fake<IRepositorioRecorrido>();
            A.CallTo(() => repositorio.TablasCreadas()).Returns(true);
            A.CallTo(() => repositorio.ColumnaPrimeroEscalera()).Returns(true);
            A.CallTo(() => repositorio.LeerPrimeroEscalera("1", "ALG")).Returns((bool?)null);
            A.CallTo(() => repositorio.LeerHuecosEnUso("1", "ALG")).Returns(new List<HuecoRecorrido>
            {
                new HuecoRecorrido { Pasillo = "001", Columna = "001", Fila = "004" },
                new HuecoRecorrido { Pasillo = "001", Columna = "001", Fila = "001" }
            });
            var peticion = new PeticionRecorridoAlmacenDTO
            {
                PrimeroEscalera = true,
                Calles = { new CalleRecorridoDTO { Calle = "a", Sentido = "A", Pasillos = { new PasilloRecorridoAlmacenDTO { Pasillo = "001" } } } }
            };

            RecorridoAlmacenDTO vista = await new ServicioRecorridoAlmacen(repositorio).Guardar("1", "ALG", peticion, "Santiago", true);

            Assert.IsTrue(vista.Ensayo);
            Assert.IsTrue(vista.PrimeroEscalera);
            CollectionAssert.AreEqual(new[] { "Escalera", "AMano" }, vista.Tramos.Select(t => t.Nivel).ToArray());
            A.CallTo(() => repositorio.Guardar(A<string>._, A<string>._, A<RecorridoNormalizado>._, A<string>._)).MustNotHaveHappened();
        }
    }
}
