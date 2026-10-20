using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#646 (corte API-2): varias PDA en la misma salida. Las faltas se suman entre PDA, así que si B da por falta
    /// lo que A ya ha cogido, se quitaba del pedido aunque fuera en la caja. Con Salidas.RecortarFaltas encendido, al
    /// terminar se quita solo lo que de verdad queda sin leer; apagado, como siempre (y el ensayo dice la diferencia).
    /// </summary>
    [TestClass]
    public class RecorteFaltasSalidaTests
    {
        private const string EMPRESA = "1";
        private IRepositorioPreparacionAlmacen repositorio;
        private RepositorioSalidasFalso escrituras;
        private TransaccionSalidaFalsa tx;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            escrituras = new RepositorioSalidasFalso();
            tx = escrituras.Transaccion;
        }

        private ServicioSalidas Servicio(bool recortar)
        {
            return new ServicioSalidas(new IOrigenSalida[]
            {
                new OrigenSalidaPicking(repositorio),
                new OrigenSalidaReposicion(repositorio)
            }, escrituras, recortarFaltas: () => recortar);
        }

        private static IPrincipal Usuario(params string[] grupos)
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Andre") }, "prueba");
            return new GenericPrincipal(identidad, grupos);
        }

        private static LineaPickingAlmacenDTO Linea(string producto, int cantidad)
            => new LineaPickingAlmacenDTO { Producto = producto, Descripcion = "Producto " + producto, Cantidad = cantidad, Pasillo = "001", Fila = "001", Columna = "001" };

        /// <summary>Picking 99700 con 3 de A (dos pedidos): Andre lee <paramref name="leeAndre"/> y Pedro da <paramref name="faltaPedro"/> por falta.</summary>
        private void PickingDeA(int leeAndre, int faltaPedro)
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 99700)).Returns(new List<LineaPickingAlmacenDTO> { Linea("A", 3) });
            A.CallTo(() => repositorio.LeerLecturasDelPicking(EMPRESA, 99700)).Returns(new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A", Usuario = "Andre", Unidades = leeAndre },
                new LecturaPickingAlmacen { Producto = "A", Usuario = "Pedro", Faltas = faltaPedro }
            });
            tx.Faltas = new List<FaltaSalida> { new FaltaSalida { Producto = "A", Hueco = "001/001/001", Cantidad = faltaPedro } };
            tx.PiezasPicking = new List<PiezaSalida>
            {
                new PiezaSalida { Linea = 5001, Pedido = 927600, Ubicacion = 77, Producto = "A", Hueco = "001001001", Cantidad = 2 },
                new PiezaSalida { Linea = 5002, Pedido = 927700, Ubicacion = 78, Producto = "A", Hueco = "001001001", Cantidad = 1 }
            };
        }

        [TestMethod]
        public async Task Encendido_UnoLeeTresYOtroDaUnaPorFaltaConTresEsperadas_NoSeQuitaNada()
        {
            PickingDeA(leeAndre: 3, faltaPedro: 1);

            ResultadoTerminarSalida resultado = await Servicio(true).Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.AreEqual(0, tx.QuitadoDeReservas.Count + tx.SacadoDePedidos.Count, "Las 3 van en la caja: no se quita nada");
            Assert.AreEqual(0, resultado.Salida.UnidadesEnFalta);
            Assert.IsTrue(resultado.Salida.Completa);
            StringAssert.Contains(resultado.Salida.Mensaje, "entero");
            Assert.AreEqual("A: se dio 1 unidad por falta, pero entre todas las PDA ya se han leído 3 de 3: no se quita nada.",
                resultado.Salida.Cambios.Single());
            Assert.AreEqual(1, tx.Terminadas.Count, "Queda terminada");
        }

        [TestMethod]
        public async Task Encendido_LasFaltasLegitimas_SeQuitanComoHoy()
        {
            // Andre lee 2 y Pedro da por falta la que de verdad no está
            PickingDeA(leeAndre: 2, faltaPedro: 1);

            ResultadoTerminarSalida resultado = await Servicio(true).Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual((5002, 1), tx.SacadoDePedidos.Single(), "Del pedido más nuevo, como siempre");
            Assert.AreEqual(1, resultado.Salida.UnidadesEnFalta);
            Assert.IsFalse(resultado.Salida.Cambios.Any(c => c.Contains("entre todas las PDA")), "Nada recortado, nada que explicar");
        }

        [TestMethod]
        public async Task Encendido_ParteDeLasFaltasLasHaCogidoOtro_SoloSeQuitaLoQueQuedaSinLeer()
        {
            // 3 esperadas: Andre lee 1 y Pedro da 3 por falta (no vio que Andre ya había cogido una)
            PickingDeA(leeAndre: 1, faltaPedro: 3);

            ResultadoTerminarSalida resultado = await Servicio(true).Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual(2, tx.SacadoDePedidos.Sum(s => s.Cantidad));
            Assert.AreEqual(2, resultado.Salida.UnidadesEnFalta);
            Assert.IsTrue(resultado.Salida.Cambios.Contains(
                "A: se dieron 3 unidades por falta, pero entre todas las PDA ya se han leído 1 de 3: solo se quitan 2 unidades."));
        }

        [TestMethod]
        public async Task Apagado_SeQuitaComoSiempre()
        {
            PickingDeA(leeAndre: 3, faltaPedro: 1);

            ResultadoTerminarSalida resultado = await Servicio(false).Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual((5002, 1), tx.SacadoDePedidos.Single(), "Sin el interruptor, lo de antes de #646");
            Assert.IsFalse((resultado.Salida.Cambios ?? new List<string>()).Any(c => c.Contains("RecortarFaltas")), "Fuera del ensayo no se dice nada");
        }

        [TestMethod]
        public async Task Apagado_ElEnsayoDiceLoQueSeQuitariaEncendido()
        {
            PickingDeA(leeAndre: 3, faltaPedro: 1);

            ResultadoTerminarSalida resultado = await Servicio(false).Terminar(EMPRESA, "PICK", 99700, Usuario("Dirección"), ensayo: true);

            Assert.IsTrue(resultado.Salida.Ensayo);
            Assert.AreEqual((5002, 1), tx.SacadoDePedidos.Single(), "El ensayo hace lo mismo que de verdad (apagado)");
            Assert.IsTrue(resultado.Salida.Cambios.Contains(
                "Con Salidas.RecortarFaltas encendido, de A se quitarían 0 unidades en vez de 1 unidad: entre todas las PDA ya se han leído 3 de 3."));
        }

        [TestMethod]
        public async Task Encendido_EnUnaReposicion_TampocoSeQuitaLoQueOtroYaHaCogido()
        {
            A.CallTo(() => repositorio.LeerReposicionSalida(EMPRESA, 80872)).Returns(new ReposicionSalida
            {
                Destino = "REI",
                Lineas = new List<LineaPickingAlmacenDTO> { Linea("A", 3) }
            });
            A.CallTo(() => repositorio.LeerLecturasDeSalida(EMPRESA, "REPO", 80872)).Returns(new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A", Usuario = "Andre", Unidades = 3 },
                new LecturaPickingAlmacen { Producto = "A", Usuario = "Pedro", Faltas = 1 }
            });
            tx.TraspasosDelDiario = new List<int> { 80872 };
            tx.Faltas = new List<FaltaSalida> { new FaltaSalida { Producto = "A", Cantidad = 1 } };
            tx.PiezasReposicion = new List<PiezaSalida> { new PiezaSalida { Linea = 1, Producto = "A", Ubicacion = 5, Hueco = "001001001", Cantidad = 3 } };

            ResultadoTerminarSalida resultado = await Servicio(true).Terminar(EMPRESA, "REPO", 80872, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.AreEqual(0, tx.QuitadoDeReposicion.Count, "Las 3 salen: no se quita nada de la salida ni de la entrada");
            Assert.AreEqual(1, tx.Contabilizados.Count);
            Assert.IsTrue(resultado.Salida.Cambios.First().Contains("no se quita nada"));
        }

        [TestMethod]
        public void RecortarFaltas_QuitaPrimeroLoQueNoTieneHuecoYDespuesDelUltimoHueco()
        {
            var netas = new List<FaltaSalida>
            {
                new FaltaSalida { Producto = "A", Hueco = "001001001", Cantidad = 1 },
                new FaltaSalida { Producto = "A", Hueco = "002001001", Cantidad = 2 },
                new FaltaSalida { Producto = "A", Hueco = null, Cantidad = 1 },
                new FaltaSalida { Producto = "B", Cantidad = 1 }
            };
            var productos = new List<DiferenciaPreparacionDTO>
            {
                // A: 6 esperadas y 4 leídas: quedan 2 sin leer (no 4)
                new DiferenciaPreparacionDTO { Producto = "A", Esperado = 6, Leido = 4 },
                new DiferenciaPreparacionDTO { Producto = "B", Esperado = 1, Leido = 0 }
            };

            ResultadoRecorteFaltas recorte = PlanificadorFaltasSalida.RecortarFaltas(netas, productos);

            Assert.AreEqual(1, recorte.Faltas.Single(f => f.Producto == "A" && f.Hueco == "001001001").Cantidad, "El primer hueco no se toca");
            Assert.AreEqual(2, recorte.Faltas.Where(f => f.Producto == "A").Sum(f => f.Cantidad));
            Assert.IsFalse(recorte.Faltas.Any(f => f.Producto == "A" && f.Hueco == null), "Lo sin hueco es lo primero que se quita");
            Assert.AreEqual(1, recorte.Faltas.Single(f => f.Producto == "A" && f.Hueco == "002001001").Cantidad, "Después, del último hueco");
            Assert.AreEqual(1, recorte.Faltas.Single(f => f.Producto == "B").Cantidad, "B no se ha leído: su falta es de verdad");
            Assert.AreEqual(1, recorte.Recortes.Count);
            Assert.AreEqual(4, recorte.Recortes[0].Netas);
            Assert.AreEqual(2, recorte.Recortes[0].Efectivas);
        }
    }
}
