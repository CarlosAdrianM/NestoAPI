using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Aparatos;
using NestoAPI.Infraestructure.Inmovilizados;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Aparatos
{
    /// <summary>NestoAPI#913, corte 1: la historia de un aparato y los descuadres por aparato (solo lectura).</summary>
    [TestClass]
    public class AparatosTests
    {
        private static readonly DateTime D1 = new DateTime(2025, 3, 1);
        private static readonly DateTime D2 = new DateTime(2026, 6, 10);
        private static readonly DateTime D3 = new DateTime(2026, 11, 17);

        private static MovimientoAparatoDTO M(TipoMovimientoAparato tipo, DateTime? fecha, string tercero = null, int? pedido = null, string nombre = null,
            bool anulado = false, string texto = null)
            => new MovimientoAparatoDTO { Tipo = tipo, Fecha = fecha, Tercero = tercero, Pedido = pedido, NombreTercero = nombre, Anulado = anulado, Texto = texto };

        private static RegistroAparatoDTO R(int numero, string producto = "36929", string serie = "A32046", string cliente = null, string inmovilizado = null, int? cuotas = null)
            => new RegistroAparatoDTO { Numero = numero, Producto = producto, NumeroSerie = serie, Cliente = cliente, Inmovilizado = inmovilizado, Cuotas = cuotas };

        // ---------------------------------------------------------------- la regla de la situación

        [TestMethod]
        public void Situacion_SinMovimientos_SinRastro()
        {
            Assert.AreEqual(SituacionAparato.SinRastro, ReglaSituacionAparato.Calcular(new List<MovimientoAparatoDTO>(), null));
            Assert.AreEqual(SituacionAparato.SinRastro, ReglaSituacionAparato.Calcular(null, R(1)));
        }

        [TestMethod]
        public void Situacion_SoloElRegistro_NoDecide()
        {
            Assert.AreEqual(SituacionAparato.SinRastro, ReglaSituacionAparato.Calcular(new[] { M(TipoMovimientoAparato.RegistroAlquiler, D3, "22709") }, R(1, cliente: "22709")));
        }

        [TestMethod]
        public void Situacion_Comprado_EnStock()
        {
            Assert.AreEqual(SituacionAparato.EnStock, ReglaSituacionAparato.Calcular(new[] { M(TipoMovimientoAparato.Compra, D1, "P1") }, null));
        }

        [TestMethod]
        public void Situacion_CompradoYVendido_Vendido()
        {
            var movs = new[] { M(TipoMovimientoAparato.Compra, D1, "P1"), M(TipoMovimientoAparato.Venta, D2, "22709") };
            Assert.AreEqual(SituacionAparato.Vendido, ReglaSituacionAparato.Calcular(movs, null));
        }

        [TestMethod]
        public void Situacion_VendidoYDespuesAbonado_VuelveAEstarEnStock()
        {
            var movs = new[] { M(TipoMovimientoAparato.Venta, D1, "22709"), M(TipoMovimientoAparato.Abono, D2, "22709") };
            Assert.AreEqual(SituacionAparato.EnStock, ReglaSituacionAparato.Calcular(movs, null));
        }

        [TestMethod]
        public void Situacion_AbonadoYRevendido_LaUltimaVentaManda()
        {
            var movs = new[] { M(TipoMovimientoAparato.Venta, D1, "1"), M(TipoMovimientoAparato.Abono, D2, "1"), M(TipoMovimientoAparato.Venta, D3, "2") };
            Assert.AreEqual(SituacionAparato.Vendido, ReglaSituacionAparato.Calcular(movs, null));
            StringAssert.StartsWith(ReglaSituacionAparato.Texto(movs, null), "Vendido al cliente 2");
        }

        [TestMethod]
        public void Situacion_ElOrdenDeLaListaNoImporta_MandaLaFecha()
        {
            var movs = new[] { M(TipoMovimientoAparato.Abono, D2, "1"), M(TipoMovimientoAparato.Venta, D1, "1") };
            Assert.AreEqual(SituacionAparato.EnStock, ReglaSituacionAparato.Calcular(movs, null));
        }

        [TestMethod]
        public void Situacion_MismaFecha_VentaYAbono_ElAbonoVaDespues()
        {
            var movs = new[] { M(TipoMovimientoAparato.Abono, D2, "1"), M(TipoMovimientoAparato.Venta, D2, "1") };
            Assert.AreEqual(SituacionAparato.EnStock, ReglaSituacionAparato.Calcular(movs, null));
        }

        [TestMethod]
        public void Situacion_MismaFecha_CompraYVenta_LaVentaVaDespues()
        {
            var movs = new[] { M(TipoMovimientoAparato.Venta, D2, "1"), M(TipoMovimientoAparato.Compra, D2, "P1") };
            Assert.AreEqual(SituacionAparato.Vendido, ReglaSituacionAparato.Calcular(movs, null));
        }

        [TestMethod]
        public void Situacion_Inmovilizado_InmovilizadoConTexto()
        {
            var movs = new[] { M(TipoMovimientoAparato.Compra, D1, "P1"), M(TipoMovimientoAparato.Inmovilizado, D3, "I/1824", texto: "DEPIBLEND ref.36929 n/s A32046") };
            Assert.AreEqual(SituacionAparato.Inmovilizado, ReglaSituacionAparato.Calcular(movs, null));
            Assert.AreEqual("Inmovilizado I/1824 desde el 17/11/2026 (DEPIBLEND ref.36929 n/s A32046).", ReglaSituacionAparato.Texto(movs, null));
        }

        [TestMethod]
        public void Situacion_InmovilizadoAnulado_NoDecide()
        {
            var movs = new[] { M(TipoMovimientoAparato.Compra, D1, "P1"), M(TipoMovimientoAparato.Inmovilizado, D3, "I/1824", anulado: true) };
            Assert.AreEqual(SituacionAparato.EnStock, ReglaSituacionAparato.Calcular(movs, null));
        }

        [TestMethod]
        public void Situacion_InmovilizadoYDespuesVendido_Vendido()
        {
            var movs = new[] { M(TipoMovimientoAparato.Inmovilizado, D1, "I/1"), M(TipoMovimientoAparato.Venta, D2, "22709") };
            Assert.AreEqual(SituacionAparato.Vendido, ReglaSituacionAparato.Calcular(movs, null));
        }

        [TestMethod]
        public void Situacion_VentaConRegistroDeCuotasDelMismoCliente_Alquilado()
        {
            var movs = new[] { M(TipoMovimientoAparato.Venta, D2, "22709") };
            Assert.AreEqual(SituacionAparato.Alquilado, ReglaSituacionAparato.Calcular(movs, R(5, cliente: "22709  ", cuotas: 24)));
            Assert.AreEqual(SituacionAparato.Vendido, ReglaSituacionAparato.Calcular(movs, R(5, cliente: "11111", cuotas: 24)));
            Assert.AreEqual(SituacionAparato.Vendido, ReglaSituacionAparato.Calcular(movs, R(5, cliente: "22709", cuotas: 0)));
        }

        [TestMethod]
        public void Situacion_DevueltoAlProveedor()
        {
            var movs = new[] { M(TipoMovimientoAparato.Compra, D1, "P1"), M(TipoMovimientoAparato.DevolucionProveedor, D2, "P1") };
            Assert.AreEqual(SituacionAparato.DevueltoAlProveedor, ReglaSituacionAparato.Calcular(movs, null));
        }

        [TestMethod]
        public void Texto_Vendido_ComoLoPideCarlos()
        {
            var movs = new[] { M(TipoMovimientoAparato.Venta, D3, "22709", 930489, "NOMBRE") };
            Assert.AreEqual("Vendido al cliente 22709 (NOMBRE) el 17/11/2026, pedido 930489.", ReglaSituacionAparato.Texto(movs, null));
        }

        [TestMethod]
        public void Texto_Abono_YSinRastroConRegistro()
        {
            Assert.AreEqual("En stock: el cliente 22709 lo devolvió el 17/11/2026, pedido 930500.",
                ReglaSituacionAparato.Texto(new[] { M(TipoMovimientoAparato.Abono, D3, "22709", 930500) }, null));
            StringAssert.Contains(ReglaSituacionAparato.Texto(new MovimientoAparatoDTO[0], R(1934)), "el registro 1934 de Alquileres dice: en stock");
        }

        [TestMethod]
        public void SegunRegistro_InmovilizadoClienteCuotasOEnStock()
        {
            Assert.AreEqual(SituacionAparato.EnStock, ReglaSituacionAparato.SegunRegistro(R(1, cliente: "  ", inmovilizado: " ")));
            Assert.AreEqual(SituacionAparato.Vendido, ReglaSituacionAparato.SegunRegistro(R(1, cliente: "1")));
            Assert.AreEqual(SituacionAparato.Alquilado, ReglaSituacionAparato.SegunRegistro(R(1, cliente: "1", cuotas: 12)));
            Assert.AreEqual(SituacionAparato.Inmovilizado, ReglaSituacionAparato.SegunRegistro(R(1, cliente: "1", inmovilizado: "1824")));
        }

        [TestMethod]
        public void Clave_SinEspaciosDeLosExtremosNiMayusculas()
        {
            Assert.IsTrue(ReglaSituacionAparato.MismaSerie(" a32046 ", "A32046"));
            Assert.IsFalse(ReglaSituacionAparato.MismaSerie("A 32046", "A32046"));
            Assert.IsFalse(ReglaSituacionAparato.MismaSerie("", ""));
        }

        // ---------------------------------------------------------------- inmovilizados: la serie del texto

        [TestMethod]
        public void Inmovilizado_SerieYReferenciaDelTexto()
        {
            Assert.AreEqual("SHE-RFS009-2511111", ReglaDescripcionEtiquetaInmovilizado.SerieEscrita("MAQUINA X ref.44276 n/s SHE-RFS009-2511111"));
            Assert.AreEqual("44276", ReglaDescripcionEtiquetaInmovilizado.ReferenciaEscrita("MAQUINA X ref.44276 n/s SHE-RFS009-2511111"));
            Assert.AreEqual("A32046", ReglaDescripcionEtiquetaInmovilizado.SerieEscrita("DEPIBLEND nº serie: A32046"));
            Assert.AreEqual("B1", ReglaDescripcionEtiquetaInmovilizado.SerieEscrita("DEPIBLEND S/N B1"));
            Assert.IsNull(ReglaDescripcionEtiquetaInmovilizado.SerieEscrita("ORDENADOR DE LA TIENDA"));
            Assert.IsNull(ReglaDescripcionEtiquetaInmovilizado.ReferenciaEscrita("DEPIBLEND n/s A1"));
        }

        private static DatosAparatos Datos()
        {
            return new DatosAparatos
            {
                Productos = new List<ProductoAparatoFila>
                {
                    new ProductoAparatoFila { Producto = "36929", Nombre = "DEPIBLEND", NecesitaNumSerie = true, EnListaAlquileres = true },
                    new ProductoAparatoFila { Producto = "44276", Nombre = "RADIOFRECUENCIA", NecesitaNumSerie = true }
                }
            };
        }

        [TestMethod]
        public void Constructor_VentasComprasYAbonosPorSigno()
        {
            DatosAparatos datos = Datos();
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 930489, Producto = "36929", NumeroSerie = "A32046 ", Cantidad = 1, FechaAlbaran = D3, Albaran = 731200, Factura = "NV2616046", Cliente = "22709", NombreCliente = "NOMBRE", Almacen = "ALG" });
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 930500, Producto = "36929", NumeroSerie = "A32046", Cantidad = -1, FechaAlbaran = D3, Cliente = "22709" });
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 1, Producto = "36929", NumeroSerie = "  ", Cantidad = 1 });
            datos.Compras.Add(new CompraAparatoFila { Pedido = 220931, Producto = "36929", NumeroSerie = "A32046", Cantidad = 1, Fecha = D1, Proveedor = "P1" });
            datos.Compras.Add(new CompraAparatoFila { Pedido = 220932, Producto = "36929", NumeroSerie = "X", Cantidad = -1, Fecha = D2, Proveedor = "P1" });

            List<MovimientoAparatoDTO> movs = ConstructorMovimientosAparato.Movimientos(datos);

            Assert.AreEqual(4, movs.Count);
            MovimientoAparatoDTO venta = movs.Single(m => m.Tipo == TipoMovimientoAparato.Venta);
            Assert.AreEqual("Pedido 930489 · albarán 731200 · factura NV2616046", venta.Documento);
            Assert.AreEqual(1, movs.Count(m => m.Tipo == TipoMovimientoAparato.Abono));
            Assert.AreEqual(1, movs.Count(m => m.Tipo == TipoMovimientoAparato.DevolucionProveedor));
        }

        [TestMethod]
        public void Constructor_Inmovilizado_ConReferenciaSoloDeEseProducto_SinReferenciaSiLaSerieEsDeUnSoloProducto()
        {
            DatosAparatos datos = Datos();
            datos.Compras.Add(new CompraAparatoFila { Pedido = 1, Producto = "36929", NumeroSerie = "A32046", Cantidad = 1, Fecha = D1 });
            datos.Compras.Add(new CompraAparatoFila { Pedido = 2, Producto = "36929", NumeroSerie = "COMUN", Cantidad = 1, Fecha = D1 });
            datos.Compras.Add(new CompraAparatoFila { Pedido = 3, Producto = "44276", NumeroSerie = "COMUN", Cantidad = 1, Fecha = D1 });
            datos.Inmovilizados.Add(new InmovilizadoAparatoFila { Numero = "1824", Descripcion = "DEPIBLEND n/s A32046", FechaCreacion = D3 });
            datos.Inmovilizados.Add(new InmovilizadoAparatoFila { Numero = "1825", Descripcion = "RADIO ref.44276 n/s COMUN", FechaCreacion = D3 });
            datos.Inmovilizados.Add(new InmovilizadoAparatoFila { Numero = "1826", Descripcion = "SIN REF n/s COMUN", FechaCreacion = D3 });
            datos.Inmovilizados.Add(new InmovilizadoAparatoFila { Numero = "1827", Descripcion = "OTRO ref.99999 n/s A32046", FechaCreacion = D3 });
            datos.Inmovilizados.Add(new InmovilizadoAparatoFila { Numero = "1828", Descripcion = "ANULADO n/s A32046", FechaCreacion = D3, Estado = -1 });

            List<MovimientoAparatoDTO> inmovilizados = ConstructorMovimientosAparato.Movimientos(datos).Where(m => m.Tipo == TipoMovimientoAparato.Inmovilizado).ToList();

            CollectionAssert.AreEquivalent(new[] { "I/1824:36929", "I/1825:44276", "I/1828:36929" }, inmovilizados.Select(m => m.Tercero + ":" + m.Producto).ToArray());
            Assert.IsTrue(inmovilizados.Single(m => m.Tercero == "I/1828").Anulado);
        }

        // ---------------------------------------------------------------- la historia

        [TestMethod]
        public void Historia_SinRastro_Null()
        {
            Assert.IsNull(ReglaHistoriaAparato.Construir("36929", "NADA", Datos()));
        }

        [TestMethod]
        public void Historia_ConRegistroRepetidoYMovimientosOrdenados()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1934, serie: "a32046"));
            datos.Registros.Add(R(1200, serie: "A32046 "));
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 930489, Producto = "36929", NumeroSerie = "A32046", Cantidad = 1, FechaAlbaran = D3, Cliente = "22709", NombreCliente = "NOMBRE" });
            datos.Compras.Add(new CompraAparatoFila { Pedido = 220931, Producto = "36929", NumeroSerie = "A32046", Cantidad = 1, Fecha = D1, Proveedor = "P1" });
            datos.NumerosSerie.Add(new NumeroSerieFila { Producto = "36929", NumeroSerie = "A32046" });

            HistoriaAparatoDTO historia = ReglaHistoriaAparato.Construir("36929", " A32046", datos);

            Assert.AreEqual(SituacionAparato.Vendido, historia.Situacion);
            Assert.AreEqual("Vendido al cliente 22709 (NOMBRE) el 17/11/2026, pedido 930489.", historia.TextoSituacion);
            Assert.AreEqual(1934, historia.Registro.Numero);
            CollectionAssert.AreEqual(new[] { 1200 }, historia.OtrosRegistros);
            Assert.IsTrue(historia.EnTablaNumerosSerie);
            Assert.AreEqual("DEPIBLEND", historia.Nombre);
            Assert.AreEqual(TipoMovimientoAparato.Compra, historia.Movimientos.First(m => m.Fecha.HasValue).Tipo);
        }

        // ---------------------------------------------------------------- los descuadres

        [TestMethod]
        public void Descuadres_RegistroEnStockPeroVendidoEInmovilizado()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1934, serie: "A32046"));
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 930489, Producto = "36929", NumeroSerie = "A32046", Cantidad = 1, FechaAlbaran = D3, Cliente = "22709" });
            datos.Registros.Add(R(1242, serie: "B1"));
            datos.Inmovilizados.Add(new InmovilizadoAparatoFila { Numero = "1824", Descripcion = "DEPIBLEND ref.36929 n/s B1", FechaCreacion = D3 });

            InformeDescuadresAparatosDTO informe = ReglaDescuadresAparatos.Calcular(datos, false);

            DescuadreAparatoDTO vendido = informe.Descuadres.Single(d => d.NumeroSerie == "A32046");
            Assert.AreEqual(MotivoDescuadreAparato.RegistroEnStockPeroVendido, vendido.Motivo);
            Assert.AreEqual(SituacionAparato.EnStock, vendido.SituacionSegunRegistro);
            Assert.AreEqual(SituacionAparato.Vendido, vendido.SituacionCalculada);
            DescuadreAparatoDTO inmovilizado = informe.Descuadres.Single(d => d.NumeroSerie == "B1");
            CollectionAssert.AreEqual(new[] { MotivoDescuadreAparato.RegistroEnStockPeroInmovilizado, MotivoDescuadreAparato.InmovilizadoSinEnlazar }, inmovilizado.Motivos);
            StringAssert.Contains(inmovilizado.TextoMotivo, "I/1824");
        }

        [TestMethod]
        public void Descuadres_LoQueCoincideNoSale()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1, serie: "S1", cliente: "22709"));
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 1, Producto = "36929", NumeroSerie = "S1", Cantidad = 1, FechaAlbaran = D3, Cliente = "22709" });
            datos.Registros.Add(R(2, serie: "S2"));
            datos.Compras.Add(new CompraAparatoFila { Pedido = 2, Producto = "36929", NumeroSerie = "S2", Cantidad = 1, Fecha = D1 });
            datos.Registros.Add(R(3, serie: "S3", inmovilizado: "1824"));
            datos.Inmovilizados.Add(new InmovilizadoAparatoFila { Numero = "1824", Descripcion = "X ref.36929 n/s S3", FechaCreacion = D3 });

            Assert.AreEqual(0, ReglaDescuadresAparatos.Calcular(datos, false).TotalDescuadres);
        }

        [TestMethod]
        public void Descuadres_RegistroConClienteSinVenta_YAbonado()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1, serie: "S1", cliente: "22709"));
            datos.Registros.Add(R(2, serie: "S2", cliente: "22709"));
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 1, Producto = "36929", NumeroSerie = "S2", Cantidad = 1, FechaAlbaran = D1, Cliente = "22709" });
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 2, Producto = "36929", NumeroSerie = "S2", Cantidad = -1, FechaAlbaran = D2, Cliente = "22709" });

            InformeDescuadresAparatosDTO informe = ReglaDescuadresAparatos.Calcular(datos, false);

            Assert.AreEqual(MotivoDescuadreAparato.RegistroConClienteSinVenta, informe.Descuadres.Single(d => d.NumeroSerie == "S1").Motivo);
            Assert.AreEqual(MotivoDescuadreAparato.RegistroConClientePeroDevuelto, informe.Descuadres.Single(d => d.NumeroSerie == "S2").Motivo);
        }

        [TestMethod]
        public void Descuadres_VendidoSinRegistro_SoloSiNecesitaNumSerie()
        {
            DatosAparatos datos = Datos();
            datos.Productos.Add(new ProductoAparatoFila { Producto = "11111", Nombre = "SIN MARCA", NecesitaNumSerie = false, EnListaAlquileres = true });
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 1, Producto = "44276", NumeroSerie = "R1", Cantidad = 1, FechaAlbaran = D3, Cliente = "1" });
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 2, Producto = "11111", NumeroSerie = "R2", Cantidad = 1, FechaAlbaran = D3, Cliente = "1" });

            InformeDescuadresAparatosDTO informe = ReglaDescuadresAparatos.Calcular(datos, false);

            Assert.AreEqual(1, informe.TotalDescuadres);
            Assert.AreEqual(MotivoDescuadreAparato.VendidoSinRegistro, informe.Descuadres.Single().Motivo);
        }

        [TestMethod]
        public void Descuadres_SerieRepetidaEnElRegistro()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(10, serie: "S1"));
            datos.Registros.Add(R(11, serie: " s1"));

            DescuadreAparatoDTO d = ReglaDescuadresAparatos.Calcular(datos, false).Descuadres.Single();

            Assert.AreEqual(MotivoDescuadreAparato.SerieRepetidaEnElRegistro, d.Motivo);
            CollectionAssert.AreEqual(new[] { 10, 11 }, d.Registros);
        }

        [TestMethod]
        public void Descuadres_ResumenPorProducto()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1, serie: "S1"));
            datos.Registros.Add(R(2, serie: "S2"));
            datos.Registros.Add(R(3, serie: "S3", cliente: "9"));
            datos.Compras.Add(new CompraAparatoFila { Pedido = 1, Producto = "36929", NumeroSerie = "S1", Cantidad = 1, Fecha = D1 });
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 1, Producto = "36929", NumeroSerie = "S2", Cantidad = 1, FechaAlbaran = D2, Cliente = "9" });
            datos.Stock.Add(new StockAparatoFila { Producto = "36929", Almacen = "ALG", Cantidad = 2 });
            datos.Stock.Add(new StockAparatoFila { Producto = "36929", Almacen = "PRV", Cantidad = 1 });

            ResumenProductoAparatosDTO resumen = ReglaDescuadresAparatos.Calcular(datos, false).Productos.Single(p => p.Producto == "36929");

            Assert.AreEqual(3, resumen.StockContableTotal);
            CollectionAssert.AreEqual(new[] { "ALG:2", "PRV:1" }, resumen.StockContable.Select(s => s.Almacen + ":" + s.Cantidad).ToArray());
            Assert.AreEqual(2, resumen.EnStockSegunRegistro);
            Assert.AreEqual(1, resumen.EnStockSegunMovimientos);
            Assert.AreEqual(1, resumen.Diferencia);
            Assert.AreEqual(1, resumen.DiferenciaStockContable);
        }

        [TestMethod]
        public void Descuadres_Tope2000ConAviso()
        {
            DatosAparatos datos = Datos();
            for (int i = 0; i < 2001; i++)
            {
                datos.Ventas.Add(new VentaAparatoFila { Pedido = i, Producto = "44276", NumeroSerie = "S" + i, Cantidad = 1, FechaAlbaran = D3, Cliente = "1" });
            }

            InformeDescuadresAparatosDTO informe = ReglaDescuadresAparatos.Calcular(datos, false);

            Assert.AreEqual(2001, informe.TotalDescuadres);
            Assert.AreEqual(2000, informe.Descuadres.Count);
            StringAssert.Contains(informe.Aviso, "2.001");
        }

        // ---------------------------------------------------------------- la lectura

        [TestMethod]
        public void Sql_AlcanceYSerieSinFuncionesQueNoTieneLaBD()
        {
            var historia = new FiltroAparatos { Producto = "36929", NumeroSerie = "A32046" };
            StringAssert.Contains(LectorAparatosSql.SqlVentas(historia), "p.[Número] = @producto");
            StringAssert.Contains(LectorAparatosSql.SqlVentas(historia), "UPPER(LTRIM(RTRIM(l.NumSerie))) = @serie");
            StringAssert.Contains(LectorAparatosSql.SqlCompras(new FiltroAparatos { SoloListaAlquileres = true }), "ProductosAlquiler");
            StringAssert.Contains(LectorAparatosSql.SqlRegistros(new FiltroAparatos()), "NecesitaNumSerie = 1");
            StringAssert.Contains(LectorAparatosSql.SqlStock(new FiltroAparatos()), "e.Empresa IN ('1', '3')");
            StringAssert.Contains(LectorAparatosSql.SqlInmovilizados(historia), "LIKE @serieLike");
            Assert.IsFalse(LectorAparatosSql.SqlInmovilizados(new FiltroAparatos()).Contains("@serie"));
            foreach (string sql in new[] { LectorAparatosSql.SqlVentas(historia), LectorAparatosSql.SqlCompras(historia), LectorAparatosSql.SqlRegistros(historia),
                LectorAparatosSql.SqlNumerosSerie(historia), LectorAparatosSql.SqlProductos(historia) })
            {
                StringAssert.Contains(sql, "NOLOCK");
                Assert.IsFalse(sql.Contains("STRING_SPLIT") || sql.Contains("TRY_CONVERT"));
            }
        }

        // ---------------------------------------------------------------- el controlador

        private ILectorAparatos lector;

        [TestInitialize]
        public void Preparar()
        {
            lector = A.Fake<ILectorAparatos>();
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1934, serie: "A32046"));
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 930489, Producto = "36929", NumeroSerie = "A32046", Cantidad = 1, FechaAlbaran = D3, Cliente = "22709" });
            A.CallTo(() => lector.Leer(A<FiltroAparatos>._)).Returns(datos);
        }

        private AparatosController Controlador(params Claim[] claims)
        {
            var lista = new List<Claim> { new Claim(ClaimTypes.Name, "Carlos") };
            lista.AddRange(claims);
            return new AparatosController(lector)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(lista, "Bearer"))
            };
        }

        private static Claim Grupo(string grupo) => new Claim(ClaimTypes.Role, grupo);

        [TestMethod]
        public async Task Historia_SinGrupo_403YNoLee()
        {
            var r = (NegotiatedContentResult<HttpError>)await Controlador().GetHistoria("36929", "A32046");
            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            A.CallTo(() => lector.Leer(A<FiltroAparatos>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Historia_ClienteDeLaTienda_403()
        {
            var r = (NegotiatedContentResult<HttpError>)await Controlador(Grupo("Almacén"), new Claim("cliente", "15191")).GetHistoria("36929", "A32046");
            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
        }

        [TestMethod]
        public async Task Historia_SinSerie_400()
        {
            Assert.IsInstanceOfType(await Controlador(Grupo("Compras")).GetHistoria("36929", " "), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Historia_Ok_LeeSoloEseAparato()
        {
            var ok = (OkNegotiatedContentResult<HistoriaAparatoDTO>)await Controlador(Grupo(@"NUEVAVISION\Administración")).GetHistoria(" 36929 ", "a32046");

            Assert.AreEqual(SituacionAparato.Vendido, ok.Content.Situacion);
            A.CallTo(() => lector.Leer(A<FiltroAparatos>.That.Matches(f => f.Producto == "36929" && f.NumeroSerie == "a32046" && !f.ConStock))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Historia_SinRastro_404()
        {
            var r = (NegotiatedContentResult<HttpError>)await Controlador(Grupo("Dirección")).GetHistoria("36929", "NADA");
            Assert.AreEqual(HttpStatusCode.NotFound, r.StatusCode);
        }

        [TestMethod]
        public async Task Descuadres_Ok_ConStockYAlcance()
        {
            var ok = (OkNegotiatedContentResult<InformeDescuadresAparatosDTO>)await Controlador(Grupo("Informática")).GetDescuadres(soloListaAlquileres: true);

            Assert.IsTrue(ok.Content.SoloListaAlquileres);
            Assert.AreEqual(MotivoDescuadreAparato.RegistroEnStockPeroVendido, ok.Content.Descuadres.Single().Motivo);
            A.CallTo(() => lector.Leer(A<FiltroAparatos>.That.Matches(f => f.SoloListaAlquileres && f.ConStock && f.Producto == null))).MustHaveHappenedOnceExactly();
        }
    }
}
