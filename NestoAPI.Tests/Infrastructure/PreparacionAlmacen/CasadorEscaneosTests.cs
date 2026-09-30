using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: las reglas de la preparación con lector (Ariadna). Lo esperado frente a lo leído,
    /// a qué producto corresponde un código y en qué orden se recorre el almacén.
    /// </summary>
    [TestClass]
    public class CasadorEscaneosTests
    {
        private static CasadorEscaneos.Cantidad C(string producto, int unidades)
        {
            return new CasadorEscaneos.Cantidad { Producto = producto, Unidades = unidades };
        }

        private static KeyValuePair<string, string> P(string producto, string codigo)
        {
            return new KeyValuePair<string, string>(producto, codigo);
        }

        private static LineaPickingAlmacenDTO L(string producto, string pasillo, string fila, string columna, string codigo = "8400000000001")
        {
            return new LineaPickingAlmacenDTO { Producto = producto, Pasillo = pasillo, Fila = fila, Columna = columna, CodigoBarras = codigo, Cantidad = 1 };
        }

        [TestMethod]
        public void Casar_TodoLeido_EstaCompleto()
        {
            var diferencias = CasadorEscaneos.Casar(new[] { C("44194", 2), C("18004", 1) }, new[] { C("44194", 2), C("18004", 1) });

            Assert.IsTrue(CasadorEscaneos.EstaCompleto(diferencias));
            Assert.IsTrue(diferencias.All(d => d.Diferencia == 0));
        }

        [TestMethod]
        public void Casar_FaltaUnaUnidad_DiferenciaNegativa()
        {
            var diferencias = CasadorEscaneos.Casar(new[] { C("44194", 2) }, new[] { C("44194", 1) });

            Assert.AreEqual(-1, diferencias.Single().Diferencia);
            Assert.IsFalse(CasadorEscaneos.EstaCompleto(diferencias));
        }

        [TestMethod]
        public void Casar_SobraUnaUnidad_DiferenciaPositiva()
        {
            var diferencias = CasadorEscaneos.Casar(new[] { C("44194", 2) }, new[] { C("44194", 3) });

            Assert.AreEqual(1, diferencias.Single().Diferencia);
            Assert.IsFalse(CasadorEscaneos.EstaCompleto(diferencias));
        }

        [TestMethod]
        public void Casar_ProductoQueNoEstabaEnElPedido_SaleComoAjeno()
        {
            var diferencias = CasadorEscaneos.Casar(new[] { C("44194", 1) }, new[] { C("44194", 1), C("99999", 1) });

            DiferenciaPreparacionDTO ajeno = diferencias.Single(d => d.Producto == "99999");
            Assert.IsTrue(ajeno.Ajeno);
            Assert.IsFalse(CasadorEscaneos.EstaCompleto(diferencias), "Una caja con un producto ajeno no está bien preparada");
        }

        [TestMethod]
        public void Casar_AjenoCuyaLecturaSeDeshace_Desaparece()
        {
            // El mozo lee por error y lo deshace con una lectura en negativo
            var diferencias = CasadorEscaneos.Casar(new[] { C("44194", 1) }, new[] { C("44194", 1), C("99999", 1), C("99999", -1) });

            Assert.AreEqual(1, diferencias.Count);
            Assert.IsTrue(CasadorEscaneos.EstaCompleto(diferencias));
        }

        [TestMethod]
        public void Casar_VariasLineasDelMismoProductoYRellenoDelChar_SeSuman()
        {
            var diferencias = CasadorEscaneos.Casar(new[] { C("44194          ", 1), C("44194", 2) }, new[] { C("44194", 3) });

            Assert.AreEqual(3, diferencias.Single().Esperado);
            Assert.AreEqual(0, diferencias.Single().Diferencia);
        }

        [TestMethod]
        public void Casar_SinNadaQuePreparar_NoEstaCompleto()
        {
            Assert.IsFalse(CasadorEscaneos.EstaCompleto(CasadorEscaneos.Casar(null, null)));
        }

        [TestMethod]
        public void ResolverCodigo_UnSoloProducto_Unico()
        {
            var resolucion = CasadorEscaneos.ResolverCodigo(" 8436620930427 ", new[] { P("44194", "8436620930427"), P("18004", "8710505999670") });

            Assert.AreEqual(CasadorEscaneos.TipoResolucion.Unico, resolucion.Tipo);
            Assert.AreEqual("44194", resolucion.Productos.Single());
        }

        [TestMethod]
        public void ResolverCodigo_DosProductosDelPedidoCompartenCodigo_Duplicado()
        {
            var resolucion = CasadorEscaneos.ResolverCodigo("111", new[] { P("A", "111"), P("B", "111"), P("C", "222") });

            Assert.AreEqual(CasadorEscaneos.TipoResolucion.Duplicado, resolucion.Tipo);
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, resolucion.Productos);
        }

        [TestMethod]
        public void ResolverCodigo_ElMismoProductoEnDosLineas_NoEsDuplicado()
        {
            var resolucion = CasadorEscaneos.ResolverCodigo("111", new[] { P("A", "111"), P("A  ", "111") });

            Assert.AreEqual(CasadorEscaneos.TipoResolucion.Unico, resolucion.Tipo);
        }

        [TestMethod]
        public void ResolverCodigo_CodigoQueNoEstaEnLaLista_Ajeno()
        {
            Assert.AreEqual(CasadorEscaneos.TipoResolucion.Ajeno,
                CasadorEscaneos.ResolverCodigo("999", new[] { P("A", "111"), P("B", null) }).Tipo);
            Assert.AreEqual(CasadorEscaneos.TipoResolucion.Ajeno, CasadorEscaneos.ResolverCodigo("", new[] { P("B", null) }).Tipo);
        }

        [TestMethod]
        public void OrdenarRecorrido_VaPorPasilloColumnaYFila_NoPorFila()
        {
            // Carlos, 30/09/26: se enseña pasillo/fila/columna pero se recorre pasillo, columna, fila,
            // para ir avanzando columna a columna dentro del pasillo.
            var lineas = new[]
            {
                L("C", "003", "001", "010"),
                L("B", "003", "007", "002"),
                L("A", "001", "005", "016"),
                L("D", "003", "002", "002")
            };

            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(lineas);

            CollectionAssert.AreEqual(new[] { "A", "D", "B", "C" }, recorrido.Select(l => l.Producto).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, recorrido.Select(l => l.Orden).ToList());
        }

        [TestMethod]
        public void OrdenarRecorrido_LaUbicacionSeEnsenaPasilloFilaColumna()
        {
            LineaPickingAlmacenDTO linea = CasadorEscaneos.OrdenarRecorrido(new[] { L("A", "001", "002", "004") }).Single();

            Assert.AreEqual("001/002/004", linea.Ubicacion);
        }

        [TestMethod]
        public void OrdenarRecorrido_LoQueNoTieneUbicacion_VaAlFinal()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[] { L("SIN", null, null, null), L("A", "009", "003", "008") });

            Assert.AreEqual("SIN", recorrido.Last().Producto);
            Assert.IsNull(recorrido.Last().Ubicacion);
        }

        [TestMethod]
        public void OrdenarRecorrido_MarcaLosProductosSinCodigoYLosCodigosDuplicados()
        {
            // El 28 % del catálogo no tiene código de barras (las limas, por ejemplo): no es una excepción
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                L("LIMA", "001", "001", "001", codigo: "   "),
                L("A", "001", "001", "002", codigo: "111"),
                L("B", "001", "001", "003", codigo: "111"),
                L("C", "001", "001", "004", codigo: "222")
            });

            Assert.IsTrue(recorrido.Single(l => l.Producto == "LIMA").SinCodigo);
            Assert.IsNull(recorrido.Single(l => l.Producto == "LIMA").CodigoBarras);
            Assert.IsTrue(recorrido.Single(l => l.Producto == "A").CodigoDuplicado);
            Assert.IsTrue(recorrido.Single(l => l.Producto == "B").CodigoDuplicado);
            Assert.IsFalse(recorrido.Single(l => l.Producto == "C").CodigoDuplicado);
        }

        private static EscaneoAlmacenDTO Escaneo()
        {
            return new EscaneoAlmacenDTO
            {
                IdCliente = Guid.NewGuid(),
                Picking = 99633,
                Pedido = 926940,
                Producto = "44194",
                Fase = "PACK",
                Cantidad = 1,
                Metodo = "SCAN",
                Bulto = 1,
                FechaEscaneo = new DateTime(2026, 9, 30, 12, 0, 0)
            };
        }

        [TestMethod]
        public void MotivoDeRechazo_EscaneoCorrecto_Null()
        {
            Assert.IsNull(CasadorEscaneos.MotivoDeRechazo(Escaneo()));
        }

        [TestMethod]
        public void MotivoDeRechazo_LecturaEnNegativoParaDeshacer_EsValida()
        {
            EscaneoAlmacenDTO escaneo = Escaneo();
            escaneo.Cantidad = -1;

            Assert.IsNull(CasadorEscaneos.MotivoDeRechazo(escaneo));
        }

        [TestMethod]
        public void MotivoDeRechazo_PickingPorOlaSinPedido_EsValido()
        {
            EscaneoAlmacenDTO escaneo = Escaneo();
            escaneo.Fase = "pick";
            escaneo.Pedido = null;

            Assert.IsNull(CasadorEscaneos.MotivoDeRechazo(escaneo));
        }

        [TestMethod]
        public void MotivoDeRechazo_DatosMalos_DiceCual()
        {
            EscaneoAlmacenDTO sinId = Escaneo(); sinId.IdCliente = Guid.Empty;
            EscaneoAlmacenDTO sinProducto = Escaneo(); sinProducto.Producto = " ";
            EscaneoAlmacenDTO faseRara = Escaneo(); faseRara.Fase = "OTRA";
            EscaneoAlmacenDTO metodoRaro = Escaneo(); metodoRaro.Metodo = "OJO";
            EscaneoAlmacenDTO cantidadCero = Escaneo(); cantidadCero.Cantidad = 0;
            EscaneoAlmacenDTO packSinPedido = Escaneo(); packSinPedido.Pedido = null;
            EscaneoAlmacenDTO sinFecha = Escaneo(); sinFecha.FechaEscaneo = default(DateTime);

            StringAssert.Contains(CasadorEscaneos.MotivoDeRechazo(sinId), "IdCliente");
            StringAssert.Contains(CasadorEscaneos.MotivoDeRechazo(sinProducto), "producto");
            StringAssert.Contains(CasadorEscaneos.MotivoDeRechazo(faseRara), "PICK o PACK");
            StringAssert.Contains(CasadorEscaneos.MotivoDeRechazo(metodoRaro), "SCAN, MANUAL o FALTA");
            StringAssert.Contains(CasadorEscaneos.MotivoDeRechazo(cantidadCero), "cantidad");
            StringAssert.Contains(CasadorEscaneos.MotivoDeRechazo(packSinPedido), "pedido");
            StringAssert.Contains(CasadorEscaneos.MotivoDeRechazo(sinFecha), "fecha");
            Assert.IsNotNull(CasadorEscaneos.MotivoDeRechazo(null));
        }
    }
}
