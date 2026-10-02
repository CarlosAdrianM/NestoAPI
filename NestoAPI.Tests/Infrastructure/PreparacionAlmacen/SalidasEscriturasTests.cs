using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>Lo que se escribe al terminar una salida, apuntado en memoria.</summary>
    public class TransaccionSalidaFalsa : ITransaccionSalida
    {
        public List<FaltaSalida> Faltas { get; set; } = new List<FaltaSalida>();
        public List<PiezaSalida> PiezasPicking { get; set; } = new List<PiezaSalida>();
        public List<PiezaSalida> PiezasReposicion { get; set; } = new List<PiezaSalida>();
        public DiarioSalidaReposicion Diario { get; set; } = new DiarioSalidaReposicion { Almacen = "ALG", Diario = "General", Destino = "REI" };
        public List<int> TraspasosDelDiario { get; set; } = new List<int>();
        public Exception FallarAlContabilizar { get; set; }

        public List<(PiezaSalida Pieza, int Cantidad, string Usuario)> QuitadoDeReservas { get; } = new List<(PiezaSalida, int, string)>();
        public List<(int Linea, int Cantidad)> SacadoDePedidos { get; } = new List<(int, int)>();
        public List<(PiezaSalida Pieza, int Cantidad)> QuitadoDeReposicion { get; } = new List<(PiezaSalida, int)>();
        public List<(string Diario, string Usuario)> Contabilizados { get; } = new List<(string, string)>();
        public int Fotos { get; private set; }

        public Task<List<FaltaSalida>> LeerFaltas(string empresa, string tipoOrigen, int numero) => Task.FromResult(Faltas);
        public Task<List<PiezaSalida>> LeerPiezasPicking(string empresa, int picking) => Task.FromResult(PiezasPicking);
        public Task<List<int>> PedidosDelPicking(string empresa, int picking) => Task.FromResult(PiezasPicking.Select(p => p.Pedido).Distinct().ToList());

        public Task QuitarDeLaReserva(PiezaSalida pieza, int cantidad, string usuario)
        {
            QuitadoDeReservas.Add((pieza, cantidad, usuario));
            return Task.CompletedTask;
        }

        public Task SacarDelPedido(int lineaPedido, int cantidadQueFalta)
        {
            SacadoDePedidos.Add((lineaPedido, cantidadQueFalta));
            return Task.CompletedTask;
        }

        public Task<List<PiezaSalida>> LeerPiezasReposicion(string empresa, int traspaso) => Task.FromResult(PiezasReposicion);

        public Task QuitarDeLaReposicion(string empresa, int traspaso, PiezaSalida pieza, int cantidad, string usuario)
        {
            QuitadoDeReposicion.Add((pieza, cantidad));
            return Task.CompletedTask;
        }

        public Task<DiarioSalidaReposicion> DiarioDeSalida(string empresa, int traspaso) => Task.FromResult(Diario);
        public Task<List<int>> TraspasosEnDiario(string empresa, string almacen, string diario) => Task.FromResult(TraspasosDelDiario);

        public Task Contabilizar(string empresa, string diario, string usuario)
        {
            if (FallarAlContabilizar != null)
            {
                throw FallarAlContabilizar;
            }
            Contabilizados.Add((diario, usuario));
            return Task.CompletedTask;
        }

        public Task<DateTime> AhoraEnBaseDeDatos() => Task.FromResult(new DateTime(2026, 10, 3, 10, 0, 0));

        public Task<List<FilaEnsayoDTO>> FotoPicking(string empresa, IReadOnlyCollection<int> pedidos, IReadOnlyCollection<string> productos, DateTime desde)
            => Task.FromResult(Foto());

        public Task<List<FilaEnsayoDTO>> FotoReposicion(string empresa, int traspaso, IReadOnlyCollection<string> productos, DateTime desde)
            => Task.FromResult(Foto());

        private List<FilaEnsayoDTO> Foto()
        {
            Fotos++;
            return new List<FilaEnsayoDTO>
            {
                new FilaEnsayoDTO { Tabla = "Prueba", Clave = Fotos.ToString(), Datos = $"Escrituras={QuitadoDeReservas.Count + SacadoDePedidos.Count + QuitadoDeReposicion.Count + Contabilizados.Count}" }
            };
        }
    }

    /// <summary>Hace el trabajo con la transacción falsa y apunta si se iba a deshacer siempre (ensayo).</summary>
    public class RepositorioSalidasFalso : IRepositorioSalidas
    {
        public TransaccionSalidaFalsa Transaccion { get; } = new TransaccionSalidaFalsa();
        public List<bool> DeshacerSiempre { get; } = new List<bool>();

        public Task<ResultadoTerminarSalidaDTO> EnTransaccion(Func<ITransaccionSalida, Task<ResultadoTerminarSalidaDTO>> trabajo, bool deshacerSiempre)
        {
            DeshacerSiempre.Add(deshacerSiempre);
            return trabajo(Transaccion);
        }
    }

    /// <summary>
    /// NestoAPI#556: al terminar una salida se quita lo que falta (picking: de la reserva y del pedido; reposición: de la
    /// salida y de la entrada del traspaso) y, en una reposición, se contabiliza la salida. Todo se puede ENSAYAR: el
    /// mismo código dentro de una transacción que se deshace siempre, con las filas antes y después.
    /// </summary>
    [TestClass]
    public class SalidasEscriturasTests
    {
        private const string EMPRESA = "1";
        private IRepositorioPreparacionAlmacen repositorio;
        private RepositorioSalidasFalso escrituras;
        private TransaccionSalidaFalsa tx;
        private ServicioSalidas servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            escrituras = new RepositorioSalidasFalso();
            tx = escrituras.Transaccion;
            servicio = new ServicioSalidas(new IOrigenSalida[]
            {
                new OrigenSalidaPicking(repositorio),
                new OrigenSalidaReposicion(repositorio)
            }, escrituras);
        }

        private static IPrincipal Usuario(params string[] grupos)
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Andre") }, "prueba");
            return new GenericPrincipal(identidad, grupos);
        }

        private static LineaPickingAlmacenDTO Linea(string producto, int cantidad, string pasillo)
            => new LineaPickingAlmacenDTO { Producto = producto, Descripcion = "Producto " + producto, CodigoBarras = "84" + producto, Cantidad = cantidad, Pasillo = pasillo, Fila = "001", Columna = "001" };

        private void PickingConUnaFaltaDeA()
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 99700)).Returns(new List<LineaPickingAlmacenDTO> { Linea("A", 3, "001") });
            A.CallTo(() => repositorio.LeerLecturasDelPicking(EMPRESA, 99700)).Returns(new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A", Unidades = 2, Faltas = 1 }
            });
            tx.Faltas = new List<FaltaSalida> { new FaltaSalida { Producto = "A", Hueco = "001/001/001", Cantidad = 1 } };
            tx.PiezasPicking = new List<PiezaSalida>
            {
                new PiezaSalida { Linea = 5001, Pedido = 927600, Ubicacion = 77, Producto = "A", Hueco = "001001001", Cantidad = 2 },
                new PiezaSalida { Linea = 5002, Pedido = 927700, Ubicacion = 78, Producto = "A", Hueco = "001001001", Cantidad = 1 }
            };
        }

        private void ReposicionRecogida(params LecturaPickingAlmacen[] lecturas)
        {
            A.CallTo(() => repositorio.LeerReposicionSalida(EMPRESA, 80872)).Returns(new ReposicionSalida
            {
                Destino = "REI",
                Lineas = new List<LineaPickingAlmacenDTO> { Linea("A", 3, "001") }
            });
            A.CallTo(() => repositorio.LeerLecturasDeSalida(EMPRESA, "REPO", 80872)).Returns(lecturas.ToList());
            tx.TraspasosDelDiario = new List<int> { 80872 };
        }

        [TestMethod]
        public async Task Terminar_PickingConFalta_LaQuitaDeLaReservaYDelPedidoMasNuevo()
        {
            PickingConUnaFaltaDeA();

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.AreEqual(78, tx.QuitadoDeReservas.Single().Pieza.Ubicacion, "La reserva del pedido más nuevo");
            Assert.AreEqual(1, tx.QuitadoDeReservas.Single().Cantidad);
            Assert.AreEqual("NUEVAVISION\\Andre", tx.QuitadoDeReservas.Single().Usuario);
            Assert.AreEqual((5002, 1), tx.SacadoDePedidos.Single());
            Assert.AreEqual(false, escrituras.DeshacerSiempre.Single(), "De verdad: se guarda");
            StringAssert.Contains(resultado.Salida.Mensaje, "pendiente de ubicar");
            Assert.IsTrue(resultado.Salida.Cambios.Any(c => c.Contains("927700")));
        }

        [TestMethod]
        public async Task Terminar_PickingSinFaltas_NoTocaNada()
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 99700)).Returns(new List<LineaPickingAlmacenDTO> { Linea("A", 3, "001") });
            A.CallTo(() => repositorio.LeerLecturasDelPicking(EMPRESA, 99700)).Returns(new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A", Unidades = 3 }
            });

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.AreEqual(0, tx.QuitadoDeReservas.Count + tx.SacadoDePedidos.Count);
        }

        [TestMethod]
        public async Task Terminar_FaltaEnUnaLineaConProductoEnCarpeta_NoSeTocaYSeDiceQueHayQueQuitarlaAMano()
        {
            PickingConUnaFaltaDeA();
            tx.PiezasPicking = new List<PiezaSalida>
            {
                new PiezaSalida { Linea = 5002, Pedido = 927700, Ubicacion = 78, Producto = "A", Hueco = "001001001", Cantidad = 3, QuitarAMano = true }
            };

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual(0, tx.QuitadoDeReservas.Count + tx.SacadoDePedidos.Count);
            StringAssert.Contains(resultado.Salida.Mensaje, "a mano");
        }

        [TestMethod]
        public async Task Terminar_UnaReposicion_ContabilizaLaSalidaConElUsuario()
        {
            ReposicionRecogida(new LecturaPickingAlmacen { Producto = "A", Unidades = 3 });

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "REPO", 80872, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.AreEqual(("General", "NUEVAVISION\\Andre"), tx.Contabilizados.Single());
            Assert.AreEqual(0, tx.QuitadoDeReposicion.Count);
            StringAssert.Contains(resultado.Salida.Mensaje, "REI");
        }

        [TestMethod]
        public async Task Terminar_UnaReposicionConFalta_LaQuitaAntesDeContabilizar()
        {
            ReposicionRecogida(new LecturaPickingAlmacen { Producto = "A", Unidades = 2, Faltas = 1 });
            tx.Faltas = new List<FaltaSalida> { new FaltaSalida { Producto = "A", Cantidad = 1 } };
            tx.PiezasReposicion = new List<PiezaSalida>
            {
                new PiezaSalida { Linea = 561387000, Ubicacion = 322199801, Producto = "A", Hueco = "001001001", Cantidad = 3 }
            };

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "REPO", 80872, Usuario("Almacén"));

            Assert.AreEqual(1, tx.QuitadoDeReposicion.Single().Cantidad);
            Assert.AreEqual(1, tx.Contabilizados.Count);
            StringAssert.Contains(resultado.Salida.Mensaje, "falta");
        }

        [TestMethod]
        public async Task Terminar_UnaReposicionQueNoSaleNada_NoContabilizaUnDiarioVacio()
        {
            ReposicionRecogida(new LecturaPickingAlmacen { Producto = "A", Faltas = 3 });
            tx.Faltas = new List<FaltaSalida> { new FaltaSalida { Producto = "A", Cantidad = 3 } };
            tx.PiezasReposicion = new List<PiezaSalida> { new PiezaSalida { Linea = 561387000, Producto = "A", Cantidad = 3 } };

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "REPO", 80872, Usuario("Almacén"));

            Assert.AreEqual(3, tx.QuitadoDeReposicion.Single().Cantidad);
            Assert.AreEqual(0, tx.Contabilizados.Count);
            StringAssert.Contains(resultado.Salida.Mensaje, "No ha salido nada");
        }

        [TestMethod]
        public async Task Terminar_UnaReposicionConOtroTraspasoEnElDiario_NoSeHaceNada()
        {
            ReposicionRecogida(new LecturaPickingAlmacen { Producto = "A", Unidades = 3 });
            tx.TraspasosDelDiario = new List<int> { 80872, 80880 };

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => servicio.Terminar(EMPRESA, "REPO", 80872, Usuario("Almacén")));

            StringAssert.Contains(ex.Message, "80880");
            Assert.AreEqual(0, tx.Contabilizados.Count);
        }

        [TestMethod]
        public async Task Ensayo_ElMismoCodigoEnUnaTransaccionQueSeDeshaceConLasFilasAntesYDespues()
        {
            PickingConUnaFaltaDeA();

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Dirección"), ensayo: true);

            Assert.AreEqual(true, escrituras.DeshacerSiempre.Single());
            Assert.IsTrue(resultado.Salida.Ensayo);
            Assert.AreEqual(1, tx.SacadoDePedidos.Count, "Hace exactamente lo mismo que de verdad");
            Assert.AreEqual("Escrituras=0", resultado.Salida.FilasAntes.Single().Datos);
            Assert.AreEqual("Escrituras=2", resultado.Salida.FilasDespues.Single().Datos);
            StringAssert.Contains(resultado.Salida.Mensaje, "ENSAYO");
        }

        [TestMethod]
        public async Task Ensayo_SoloAdminODireccion()
        {
            PickingConUnaFaltaDeA();

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"), ensayo: true);

            Assert.AreEqual(EstadoTerminarSalida.SinPermiso, resultado.Estado);
            Assert.AreEqual(0, escrituras.DeshacerSiempre.Count);
        }

        [TestMethod]
        public async Task Ensayo_AdminPuedeAunqueNoSeaDeAlmacen()
        {
            PickingConUnaFaltaDeA();

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Admin"), ensayo: true);

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.IsTrue(resultado.Salida.Ensayo);
        }

        [TestMethod]
        public async Task Ensayo_SiFalla_DevuelveElErrorRealYLasFilasDeAntesSinLanzar()
        {
            ReposicionRecogida(new LecturaPickingAlmacen { Producto = "A", Unidades = 3 });
            tx.FallarAlContabilizar = new Exception("No se han podido insertar los productos en la tabla ubicaciones");

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "REPO", 80872, Usuario("Dirección"), ensayo: true);

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.IsTrue(resultado.Salida.Ensayo);
            Assert.IsFalse(resultado.Salida.Terminada);
            StringAssert.Contains(resultado.Salida.ErrorEnsayo, "tabla ubicaciones");
            Assert.AreEqual(1, resultado.Salida.FilasAntes.Count);
        }
    }
}
