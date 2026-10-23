using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>La transacción del stock, falsa: se mira qué se le pide.</summary>
    public class RepositorioStockACeroFalso : IRepositorioStockACero
    {
        public ITransaccionStockACero Transaccion { get; } = A.Fake<ITransaccionStockACero>();
        public int Transacciones { get; private set; }

        public Task<T> EnTransaccion<T>(Func<ITransaccionStockACero, Task<T>> trabajo)
        {
            Transacciones++;
            return trabajo(Transaccion);
        }
    }

    /// <summary>
    /// Sugerencia 753 (Paloma, Alcobendas; Carlos 23/10/26): al terminar una reposición desde una tienda SIN ubicaciones,
    /// lo dado por «No está» deja su stock a 0 en la tienda (diario _ErrRepo). Con ubicaciones (Algete), nada: solo el hueco.
    /// Lo de la reposición (después de confirmarla, solo sus productos) está en ServicioPreparacionReposicionTests.
    /// </summary>
    [TestClass]
    public class StockACeroNoEstaTests
    {
        private const string EMPRESA = "1";
        private const int TRASPASO = 81028;
        private const string USUARIO = "NUEVAVISION\\Paloma";
        private RepositorioStockACeroFalso stock;
        private ITransaccionStockACero t;
        private bool activo;
        private int repositoriosCreados;

        [TestInitialize]
        public void Preparar()
        {
            stock = new RepositorioStockACeroFalso();
            t = stock.Transaccion;
            activo = true;
            repositoriosCreados = 0;
            A.CallTo(() => t.LeerAlmacen(EMPRESA, "ALC")).Returns(new AlmacenStockACero { Nombre = "Alcobendas", ControlUbicaciones = false });
            A.CallTo(() => t.LeerAlmacen(EMPRESA, "ALG")).Returns(new AlmacenStockACero { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => t.Mover(A<MovimientoStockDiferencia>._)).Returns(new ResultadoMovimientoStock { NumeroOrdenExtracto = 9001 });
        }

        private ServicioStockACeroNoEsta Servicio() => new ServicioStockACeroNoEsta(() => { repositoriosCreados++; return stock; }, () => activo);

        private void Stock(string producto, int cantidad, string almacen = "ALC")
        {
            A.CallTo(() => t.LeerStock(EMPRESA, almacen, producto)).Returns(cantidad);
        }

        [TestMethod]
        public async Task SinUbicaciones_PoneA0ConUnApunteDeLoQueQueda()
        {
            Stock("43763", 1);
            Stock("41980", 4);

            ResultadoStockACero resultado = await Servicio().PonerACero(EMPRESA, "ALC", TRASPASO, new[] { "43763", "41980" }, USUARIO);

            A.CallTo(() => t.Mover(A<MovimientoStockDiferencia>.That.Matches(m =>
                m.Empresa == EMPRESA && m.Almacen == "ALC" && m.Producto == "43763" && m.Cantidad == -1 && m.Traspaso == TRASPASO &&
                m.Texto == "Reposición 81028: «No está» al enviar (Paloma)" && m.Usuario == USUARIO && !m.PrimeroPendienteDeUbicar)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => t.Mover(A<MovimientoStockDiferencia>.That.Matches(m => m.Producto == "41980" && m.Cantidad == -4))).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { "Stock de Alcobendas a 0: 43763 (tenía 1), 41980 (tenía 4)." }, resultado.Cambios);
            Assert.IsNull(resultado.Aviso);
            Assert.AreEqual(3, stock.Transacciones, "una para leer el almacén y una por producto");
        }

        [TestMethod]
        public async Task ConUbicaciones_NoTocaElStock()
        {
            Stock("43763", 5, "ALG");

            ResultadoStockACero resultado = await Servicio().PonerACero(EMPRESA, "ALG", TRASPASO, new[] { "43763" }, USUARIO);

            A.CallTo(() => t.LeerStock(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => t.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
            Assert.AreEqual(0, resultado.Cambios.Count);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(-2)]
        public async Task StockCeroONegativo_NoApuntaNada(int queda)
        {
            Stock("43763", queda);

            ResultadoStockACero resultado = await Servicio().PonerACero(EMPRESA, "ALC", TRASPASO, new[] { "43763" }, USUARIO);

            A.CallTo(() => t.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
            Assert.AreEqual(0, resultado.Cambios.Count);
            Assert.IsNull(resultado.Aviso);
        }

        [TestMethod]
        public async Task SiFallaUnProducto_LosDemasSeHacenYSeAvisaSinLanzar()
        {
            Stock("43763", 1);
            Stock("41980", 2);
            A.CallTo(() => t.Mover(A<MovimientoStockDiferencia>.That.Matches(m => m.Producto == "43763")))
                .Throws(new NestoBusinessException("prdExtrProducto: hay un inventario en curso"));

            ResultadoStockACero resultado = await Servicio().PonerACero(EMPRESA, "ALC", TRASPASO, new[] { "43763", "41980" }, USUARIO);

            CollectionAssert.AreEqual(new[]
            {
                "Stock de Alcobendas a 0: 41980 (tenía 2).",
                "No se ha podido poner a 0 el stock de Alcobendas de 43763: la reposición está enviada; revise ese stock o avise a Informática."
            }, resultado.Cambios);
            Assert.AreEqual(resultado.Cambios.Last(), resultado.Aviso);
        }

        [TestMethod]
        public async Task SiFallaLeerElAlmacen_SeAvisaSinLanzar()
        {
            A.CallTo(() => t.LeerAlmacen(EMPRESA, "ALC")).Throws(new InvalidOperationException("sin conexión"));

            ResultadoStockACero resultado = await Servicio().PonerACero(EMPRESA, "ALC", TRASPASO, new[] { "43763" }, USUARIO);

            StringAssert.StartsWith(resultado.Aviso, "No se ha podido poner a 0 el stock de 43763 en ALC");
            CollectionAssert.Contains(resultado.Cambios, resultado.Aviso);
        }

        [TestMethod]
        public async Task ConElInterruptorApagado_NoTocaNada()
        {
            activo = false;
            Stock("43763", 1);

            ResultadoStockACero resultado = await Servicio().PonerACero(EMPRESA, "ALC", TRASPASO, new[] { "43763" }, USUARIO);

            Assert.AreEqual(0, repositoriosCreados);
            Assert.AreEqual(0, resultado.Cambios.Count);
        }

        [TestMethod]
        public async Task SinProductos_NoAbreNada()
        {
            ResultadoStockACero resultado = await Servicio().PonerACero(EMPRESA, "ALC", TRASPASO, new[] { " ", null }, USUARIO);

            Assert.AreEqual(0, repositoriosCreados);
            Assert.AreEqual(0, resultado.Cambios.Count);
        }

        [TestMethod]
        public void Interruptor_SinLaClaveEstaApagado()
        {
            // En el app.config de las pruebas no está la clave: Apagado
            Assert.IsFalse(InterruptorNoEstaStockACero.EstaActivo());
        }

        [TestMethod]
        public void Texto_UnUsuarioLargo_CabeEnLos50DePreExtrProducto()
        {
            string texto = ServicioStockACeroNoEsta.Texto(123456, "NUEVAVISION\\UnUsuarioConUnNombreMuyLargo");

            Assert.IsTrue(texto.Length <= ServicioStockACeroNoEsta.LONGITUD_TEXTO, texto);
            StringAssert.StartsWith(texto, "Reposición 123456: «No está» al enviar (");
            StringAssert.EndsWith(texto, ")");
        }
    }
}
