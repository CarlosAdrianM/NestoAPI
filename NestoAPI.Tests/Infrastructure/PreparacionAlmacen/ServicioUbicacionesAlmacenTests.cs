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
    /// NestoAPI#556/#559: lo recibido pendiente de ubicar y la consulta de dónde está un producto
    /// (la parte de lectura de lo que hoy hace Ariadna Vieja).
    /// </summary>
    [TestClass]
    public class ServicioUbicacionesAlmacenTests
    {
        private static FilaPendienteDeUbicar Pendiente(string producto, int cantidad, int? albaran = 125429, string codigo = "8400000000001")
        {
            return new FilaPendienteDeUbicar
            {
                Producto = producto,
                Descripcion = "PRODUCTO " + producto.Trim(),
                CodigoBarras = codigo,
                Cantidad = cantidad,
                PedidoCompra = albaran.HasValue ? 220311 : (int?)null,
                AlbaranCompra = albaran,
                DesdeCuando = new DateTime(2026, 9, 30, 10, 55, 0)
            };
        }

        private static FilaUbicacionProducto Hueco(string producto, string pasillo, string fila, string columna, int cantidad, int estado = 0)
        {
            return new FilaUbicacionProducto { Producto = producto, Pasillo = pasillo, Fila = fila, Columna = columna, Cantidad = cantidad, Estado = estado };
        }

        [TestMethod]
        public void MontarPendiente_UnProductoRecibidoEnDosAlbaranes_UnaFilaConSusDosOrigenes()
        {
            var productos = ServicioUbicacionesAlmacen.MontarPendiente(
                new[] { Pendiente("29712", 39, 125429), Pendiente("29712          ", 5, 125435) },
                new FilaUbicacionProducto[0]);

            ProductoPendienteDeUbicarDTO producto = productos.Single();
            Assert.AreEqual(44, producto.Cantidad);
            Assert.AreEqual(2, producto.Origenes.Count);
        }

        [TestMethod]
        public void MontarPendiente_SugiereLosHuecosDondeYaHayDeEseProducto()
        {
            var productos = ServicioUbicacionesAlmacen.MontarPendiente(
                new[] { Pendiente("29712", 39) },
                new[] { Hueco("29712", "004", "001", "010", 14), Hueco("29712", "004", "001", "009", 22), Hueco("OTRO", "001", "001", "001", 3) });

            List<UbicacionAlmacenDTO> huecos = productos.Single().UbicacionesActuales;
            Assert.AreEqual(2, huecos.Count);
            Assert.AreEqual("004/001/009", huecos[0].Ubicacion, "Dentro del pasillo, por columna");
        }

        [TestMethod]
        public void MontarPendiente_SeOrdenaPorElPrimerHuecoYLoQueNoTieneHuecoVaAlFinal()
        {
            var productos = ServicioUbicacionesAlmacen.MontarPendiente(
                new[] { Pendiente("NUEVO", 1), Pendiente("LEJOS", 1), Pendiente("CERCA", 1) },
                new[] { Hueco("LEJOS", "013", "000", "002", 9), Hueco("CERCA", "001", "005", "004", 7) });

            CollectionAssert.AreEqual(new[] { "CERCA", "LEJOS", "NUEVO" }, productos.Select(p => p.Producto).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, productos.Select(p => p.Orden).ToList());
        }

        [TestMethod]
        public void MontarPendiente_SinExistenciasEnNingunHueco_SugiereElUltimoDondeEstuvo()
        {
            // Casi la mitad de lo pendiente (142 de 300 productos el 30/09/26) no tiene hoy ningún hueco con género
            var productos = ServicioUbicacionesAlmacen.MontarPendiente(
                new[] { Pendiente("AGOTADO", 5), Pendiente("NUEVO", 1), Pendiente("CONSTOCK", 1) },
                new[] { Hueco("AGOTADO", "002", "007", "008", 0, estado: -1), Hueco("CONSTOCK", "011", "005", "002", 9) });

            Assert.AreEqual("002/007/008", productos.Single(p => p.Producto == "AGOTADO").UltimaUbicacion);
            Assert.AreEqual(0, productos.Single(p => p.Producto == "AGOTADO").UbicacionesActuales.Count);
            Assert.IsNull(productos.Single(p => p.Producto == "CONSTOCK").UltimaUbicacion, "Si hay hueco con género, sobra");
            Assert.IsNull(productos.Single(p => p.Producto == "NUEVO").UltimaUbicacion, "Nunca ha tenido hueco");
            CollectionAssert.AreEqual(new[] { "AGOTADO", "CONSTOCK", "NUEVO" }, productos.Select(p => p.Producto).ToList(),
                "El recorrido cuenta con el último hueco; lo nuevo, al final");
        }

        [TestMethod]
        public void MontarPendiente_UnaDevolucionDeCliente_DiceDeQuePedidoViene()
        {
            FilaPendienteDeUbicar devolucion = Pendiente("18004", 2, albaran: null);
            devolucion.PedidoVenta = 927377;

            OrigenPendienteDeUbicarDTO origen = ServicioUbicacionesAlmacen.MontarPendiente(new[] { devolucion }, null).Single().Origenes.Single();

            Assert.AreEqual(927377, origen.PedidoVenta);
            Assert.IsNull(origen.AlbaranCompra);
        }

        [TestMethod]
        public void MontarPendiente_ProductoSinCodigoDeBarras_QuedaMarcado()
        {
            ProductoPendienteDeUbicarDTO producto = ServicioUbicacionesAlmacen.MontarPendiente(
                new[] { Pendiente("LIMA", 10, codigo: "  ") }, null).Single();

            Assert.IsTrue(producto.SinCodigo);
            Assert.IsNull(producto.CodigoBarras);
        }

        [TestMethod]
        public void MontarPendiente_LoQueSumaCero_NoSale()
        {
            var productos = ServicioUbicacionesAlmacen.MontarPendiente(new[] { Pendiente("A", 3), Pendiente("A", -3, 125430) }, null);

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public async Task LeerPendienteDeUbicar_SinNadaPendiente_NoPreguntaPorLosHuecos()
        {
            var repositorio = A.Fake<IRepositorioUbicacionesAlmacen>();
            A.CallTo(() => repositorio.LeerPendienteDeUbicar("1", "ALG")).Returns(new List<FilaPendienteDeUbicar>());

            PendienteDeUbicarDTO pendiente = await new ServicioUbicacionesAlmacen(repositorio).LeerPendienteDeUbicar("1", "ALG");

            Assert.AreEqual(0, pendiente.Productos.Count);
            A.CallTo(() => repositorio.LeerHuecosDeLoPendiente(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LeerUltimoHuecoDeLoPendiente(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task BuscarProducto_SeparaLosHuecosDeLoPendienteDeUbicar()
        {
            var repositorio = A.Fake<IRepositorioUbicacionesAlmacen>();
            A.CallTo(() => repositorio.BuscarProductos("1", "8710505999670")).Returns(new List<FilaProductoAlmacen>
            {
                new FilaProductoAlmacen { Producto = "18004", Descripcion = "REJILLAS DESMAQUILLANTES ", CodigoBarras = "8710505999670  " }
            });
            A.CallTo(() => repositorio.LeerUbicacionesDelProducto("1", "ALG", "18004")).Returns(new List<FilaUbicacionProducto>
            {
                Hueco("18004", "009", "003", "008", 40),
                Hueco("18004", null, null, null, 59, estado: 2)
            });

            ProductoAlmacenDTO producto = (await new ServicioUbicacionesAlmacen(repositorio).BuscarProducto("1", "ALG", " 8710505999670 ")).Single();

            Assert.AreEqual("8710505999670", producto.CodigoBarras);
            Assert.AreEqual("009/003/008", producto.Ubicaciones.Single().Ubicacion);
            Assert.AreEqual(59, producto.PendienteDeUbicar);
        }

        // Ubicar: lo que hoy se hace con Ariadna Vieja (prdUbicar)

        private static UbicarProductoDTO PeticionDeUbicar(int cantidad = 5)
        {
            return new UbicarProductoDTO { Producto = " 18004 ", Pasillo = "9", Fila = "3", Columna = "008", Cantidad = cantidad };
        }

        private static IRepositorioUbicacionesAlmacen RepositorioConPendiente(int pendiente)
        {
            var repositorio = A.Fake<IRepositorioUbicacionesAlmacen>();
            A.CallTo(() => repositorio.LeerUbicacionesDelProducto("1", "ALG", "18004")).Returns(new List<FilaUbicacionProducto>
            {
                Hueco("18004", "009", "003", "008", 40),
                Hueco("18004", null, null, null, pendiente, estado: 2)
            });
            return repositorio;
        }

        [TestMethod]
        public void PrepararUbicacion_NormalizaElHuecoATresCifrasYElAlmacenPorDefecto()
        {
            UbicarProductoDTO ubicar = PeticionDeUbicar();

            string motivo = ServicioUbicacionesAlmacen.PrepararUbicacion(ubicar, out int tipoFiltro, out int? filtro);

            Assert.IsNull(motivo);
            Assert.AreEqual("18004", ubicar.Producto);
            Assert.AreEqual("ALG", ubicar.Almacen);
            Assert.AreEqual("009/003/008", $"{ubicar.Pasillo}/{ubicar.Fila}/{ubicar.Columna}");
            Assert.AreEqual(0, tipoFiltro);
            Assert.IsNull(filtro);
        }

        [TestMethod]
        public void PrepararUbicacion_ConLaEtiquetaDelHuecoLeida_SacaPasilloFilaYColumna()
        {
            // La etiqueta de Algete (30/09/26): «Pasillo:002 Fila:004 Columna:001», código de barras 002004001
            var ubicar = new UbicarProductoDTO { Producto = "18004", Hueco = " 002004001 ", Cantidad = 5 };

            Assert.IsNull(ServicioUbicacionesAlmacen.PrepararUbicacion(ubicar, out _, out _));

            Assert.AreEqual("002", ubicar.Pasillo);
            Assert.AreEqual("004", ubicar.Fila);
            Assert.AreEqual("001", ubicar.Columna);
        }

        [TestMethod]
        public void PrepararUbicacion_EtiquetaMalLeidaOQueNoCuadraConElHueco_DiceCual()
        {
            var malLeida = new UbicarProductoDTO { Producto = "18004", Hueco = "8710505999670", Cantidad = 5 };
            var noCuadra = new UbicarProductoDTO { Producto = "18004", Hueco = "002004001", Pasillo = "3", Fila = "4", Columna = "1", Cantidad = 5 };
            var cuadra = new UbicarProductoDTO { Producto = "18004", Hueco = "002004001", Pasillo = "2", Fila = "4", Columna = "1", Cantidad = 5 };

            StringAssert.Contains(ServicioUbicacionesAlmacen.PrepararUbicacion(malLeida, out _, out _), "etiqueta del hueco no es válida");
            StringAssert.Contains(ServicioUbicacionesAlmacen.PrepararUbicacion(noCuadra, out _, out _), "no son el mismo");
            Assert.IsNull(ServicioUbicacionesAlmacen.PrepararUbicacion(cuadra, out _, out _));
        }

        [TestMethod]
        public void LeerCodigoDeHueco_NueveCifrasOConBarras()
        {
            Assert.IsTrue(ServicioUbicacionesAlmacen.LeerCodigoDeHueco("002004001", out string pasillo, out string fila, out string columna));
            Assert.AreEqual("002/004/001", pasillo + "/" + fila + "/" + columna);
            Assert.IsTrue(ServicioUbicacionesAlmacen.LeerCodigoDeHueco("2/4/1", out pasillo, out fila, out columna));
            Assert.AreEqual("002/004/001", pasillo + "/" + fila + "/" + columna);
            Assert.IsFalse(ServicioUbicacionesAlmacen.LeerCodigoDeHueco("00200400", out _, out _, out _));
            Assert.IsFalse(ServicioUbicacionesAlmacen.LeerCodigoDeHueco("00200400A", out _, out _, out _));
            Assert.IsFalse(ServicioUbicacionesAlmacen.LeerCodigoDeHueco("2/4", out _, out _, out _));
            Assert.IsFalse(ServicioUbicacionesAlmacen.LeerCodigoDeHueco(null, out _, out _, out _));
        }

        [TestMethod]
        public void CodigoDeHueco_EsLoQueLlevaLaEtiqueta()
        {
            Assert.AreEqual("002004001", new UbicacionAlmacenDTO { Pasillo = "002", Fila = "4 ", Columna = "1" }.Codigo);
            Assert.IsNull(new UbicacionAlmacenDTO { Pasillo = "A", Fila = "4", Columna = "1" }.Codigo);
            Assert.IsNull(new UbicacionAlmacenDTO().Codigo);
        }

        [TestMethod]
        public void PrepararUbicacion_ConAlbaranDeCompra_EsElFiltroDosDelProcedimiento()
        {
            UbicarProductoDTO ubicar = PeticionDeUbicar();
            ubicar.AlbaranCompra = 125437;

            Assert.IsNull(ServicioUbicacionesAlmacen.PrepararUbicacion(ubicar, out int tipoFiltro, out int? filtro));

            Assert.AreEqual(2, tipoFiltro);
            Assert.AreEqual(125437, filtro);
        }

        [TestMethod]
        public void PrepararUbicacion_DatosMalos_DiceCual()
        {
            UbicarProductoDTO sinCantidad = PeticionDeUbicar(0);
            UbicarProductoDTO huecoRaro = PeticionDeUbicar(); huecoRaro.Pasillo = "A1";
            UbicarProductoDTO huecoLargo = PeticionDeUbicar(); huecoLargo.Columna = "1234";
            UbicarProductoDTO dosOrigenes = PeticionDeUbicar(); dosOrigenes.AlbaranCompra = 1; dosOrigenes.PedidoCompra = 2;
            UbicarProductoDTO sinProducto = PeticionDeUbicar(); sinProducto.Producto = " ";

            StringAssert.Contains(ServicioUbicacionesAlmacen.PrepararUbicacion(sinCantidad, out _, out _), "cantidad");
            StringAssert.Contains(ServicioUbicacionesAlmacen.PrepararUbicacion(huecoRaro, out _, out _), "hueco");
            StringAssert.Contains(ServicioUbicacionesAlmacen.PrepararUbicacion(huecoLargo, out _, out _), "hueco");
            StringAssert.Contains(ServicioUbicacionesAlmacen.PrepararUbicacion(dosOrigenes, out _, out _), "un origen");
            StringAssert.Contains(ServicioUbicacionesAlmacen.PrepararUbicacion(sinProducto, out _, out _), "producto");
            Assert.IsNotNull(ServicioUbicacionesAlmacen.PrepararUbicacion(null, out _, out _));
        }

        [TestMethod]
        public async Task Ubicar_HayPendienteSuficiente_LlamaAlProcedimientoConElUsuarioYDevuelveComoQueda()
        {
            IRepositorioUbicacionesAlmacen repositorio = RepositorioConPendiente(59);
            UbicarProductoDTO ubicar = PeticionDeUbicar();

            ProductoAlmacenDTO resultado = await new ServicioUbicacionesAlmacen(repositorio).Ubicar("1", ubicar, "Santiago");

            A.CallTo(() => repositorio.Ubicar("1", ubicar, 0, null, "Santiago")).MustHaveHappenedOnceExactly();
            Assert.AreEqual("18004", resultado.Producto);
            Assert.AreEqual("009/003/008", resultado.Ubicaciones.Single().Ubicacion);
        }

        [TestMethod]
        public async Task Ubicar_MasDeLoPendiente_AvisaConLoQueHayYNoLlamaAlProcedimiento()
        {
            IRepositorioUbicacionesAlmacen repositorio = RepositorioConPendiente(3);

            var ex = await Assert.ThrowsExceptionAsync<NestoAPI.Infraestructure.Exceptions.NestoBusinessException>(
                () => new ServicioUbicacionesAlmacen(repositorio).Ubicar("1", PeticionDeUbicar(5), "Santiago"));

            StringAssert.Contains(ex.Message, "solo hay 3 unidades pendientes");
            A.CallTo(() => repositorio.Ubicar(A<string>._, A<UbicarProductoDTO>._, A<int>._, A<int?>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ubicar_SinNadaPendiente_Avisa()
        {
            IRepositorioUbicacionesAlmacen repositorio = RepositorioConPendiente(0);

            var ex = await Assert.ThrowsExceptionAsync<NestoAPI.Infraestructure.Exceptions.NestoBusinessException>(
                () => new ServicioUbicacionesAlmacen(repositorio).Ubicar("1", PeticionDeUbicar(1), "Santiago"));

            StringAssert.Contains(ex.Message, "no tiene nada pendiente de ubicar");
        }

        [TestMethod]
        public async Task BuscarProducto_CodigoVacioODesproporcionado_ListaVaciaSinIrALaBaseDeDatos()
        {
            var repositorio = A.Fake<IRepositorioUbicacionesAlmacen>();
            var servicio = new ServicioUbicacionesAlmacen(repositorio);

            Assert.AreEqual(0, (await servicio.BuscarProducto("1", "ALG", "  ")).Count);
            Assert.AreEqual(0, (await servicio.BuscarProducto("1", "ALG", new string('9', 40))).Count);
            A.CallTo(() => repositorio.BuscarProductos(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- Descuadre del 46048 (30/10/26)
        // prdUbicar suma lo ubicado a TODAS las filas libres (estado 0) del hueco. El 46048 tenía dos en 004/004/012 (la
        // del picking doble del 25/08 que liberó el Fix #405/#406 y la del picking del 15/10) y el albarán de compra 125747
        // (+9) se sumó a las dos: 53 en Ubicaciones con 44 de stock. Antes de llamar a prdUbicar se juntan en una.

        [TestMethod]
        public void SqlUbicar_JuntaLasFilasLibresDelHuecoAntesDeLlamarAPrdUbicar()
        {
            string sql = RepositorioUbicacionesAlmacen.SQL_UBICAR;
            int juntar = sql.IndexOf("DELETE FROM Ubicaciones", StringComparison.Ordinal);
            int ubicar = sql.IndexOf("EXEC @resultado = prdUbicar", StringComparison.Ordinal);
            Assert.IsTrue(juntar >= 0, "Sin juntar las filas libres, prdUbicar suma la cantidad a cada una de ellas");
            Assert.IsTrue(ubicar > juntar, "Hay que juntarlas ANTES de llamar a prdUbicar");
        }

        [TestMethod]
        public void SqlJuntarLibres_SoloLasFilasLibresDeEseProductoEnEseHueco_LasMismasQueSumaPrdUbicar()
        {
            string sql = RepositorioUbicacionesAlmacen.SQL_JUNTAR_LIBRES_DEL_HUECO;
            // Las mismas que el UPDATE de prdUbicar: empresa, almacén, producto, pasillo, fila, columna y estado 0
            const string HUECO = "WHERE Empresa = @empresaHueco AND [Almacén] = @almacenHueco AND [Número] = @productoHueco"
                + " AND Pasillo = @pasilloHueco AND Fila = @filaHueco AND Columna = @columnaHueco AND Estado = 0";
            StringAssert.Contains(Normalizar(sql), Normalizar(HUECO));
            // Se queda la más antigua con la suma de todas y se borran las demás: el total del hueco no cambia
            StringAssert.Contains(sql, "@filaLibre = MIN([NºOrden]), @totalLibre = SUM(Cantidad)");
            StringAssert.Contains(sql, "HAVING COUNT(*) > 1");
            StringAssert.Contains(sql, "UPDATE Ubicaciones SET Cantidad = @totalLibre WHERE [NºOrden] = @filaLibre");
            StringAssert.Contains(Normalizar(sql), Normalizar("AND Estado = 0 AND [NºOrden] <> @filaLibre"));
            // Bloquea las filas leídas hasta el final de la transacción (dos ubicaciones a la vez en el mismo hueco)
            StringAssert.Contains(sql, "WITH (UPDLOCK, HOLDLOCK)");
            // Las columnas son char: con los parámetros nvarchar de EF se recorrería la tabla entera
            StringAssert.Contains(sql, "@productoHueco char(15) = @p2");
            // Queda anotado (mismo usuario que la ubicación)
            StringAssert.Contains(sql, "INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)");
            StringAssert.Contains(sql, "@p11);");
        }

        [TestMethod]
        public void SqlUbicar_SigueLlamandoAPrdUbicarConLosMismosParametros()
        {
            string sql = RepositorioUbicacionesAlmacen.SQL_UBICAR;
            StringAssert.Contains(sql, "EXEC @resultado = prdUbicar @Empresa = @p0, @Almacen = @p1, @Producto = @p2, @Pasillo = @p3, @Fila = @p4, @Columna = @p5,");
            StringAssert.Contains(sql, "@Cantidad = @p6, @TipoFiltro = @p7, @Filtro = @p8;");
        }

        private static string Normalizar(string sql)
        {
            return string.Join(" ", sql.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
