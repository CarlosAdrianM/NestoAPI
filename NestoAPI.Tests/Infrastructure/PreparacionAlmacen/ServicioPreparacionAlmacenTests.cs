using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: el servicio de la preparación con Ariadna, con el acceso a datos y el
    /// almacenamiento de fotos falsos.
    /// </summary>
    [TestClass]
    public class ServicioPreparacionAlmacenTests
    {
        private const string EMPRESA = "1";
        private const int PICKING = 99633;
        private const int PEDIDO = 926940;
        private static readonly byte[] JPEG = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

        private IRepositorioPreparacionAlmacen repositorio;
        private IAlmacenFotosBultos fotos;
        private ServicioPreparacionAlmacen servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            fotos = A.Fake<IAlmacenFotosBultos>();
            A.CallTo(() => fotos.Configurado).Returns(true);
            A.CallTo(() => repositorio.LeerBultoPorIdCliente(A<Guid>._)).Returns(Task.FromResult<BultoAlmacenDTO>(null));
            A.CallTo(() => repositorio.ExistePedidoEnPicking(EMPRESA, PEDIDO, PICKING)).Returns(true);
            servicio = new ServicioPreparacionAlmacen(repositorio, fotos);
        }

        private static FilaPackingAlmacen Fila(int pedido, int linea, string producto, string codigo, int cantidad,
            string cliente = "40182", string contacto = "0")
        {
            return new FilaPackingAlmacen
            {
                Pedido = pedido,
                LineaPedido = linea,
                Producto = producto,
                Descripcion = "PRODUCTO " + producto,
                CodigoBarras = codigo,
                Cantidad = cantidad,
                Cliente = cliente,
                Contacto = contacto,
                Nombre = "CLIENTE " + cliente,
                Ruta = "00 "
            };
        }

        private static EscaneoAlmacenDTO Escaneo(Guid? id = null)
        {
            return new EscaneoAlmacenDTO
            {
                IdCliente = id ?? Guid.NewGuid(),
                Picking = PICKING,
                Pedido = PEDIDO,
                Producto = "44194",
                Fase = "PACK",
                Cantidad = 1,
                Metodo = "SCAN",
                FechaEscaneo = new DateTime(2026, 9, 30, 12, 0, 0)
            };
        }

        private static FotoBultoAlmacen Foto()
        {
            return new FotoBultoAlmacen
            {
                IdCliente = Guid.NewGuid(),
                Empresa = EMPRESA,
                Pedido = PEDIDO,
                Picking = PICKING,
                Bulto = 1,
                Imagen = JPEG,
                FechaFoto = new DateTime(2026, 9, 30, 12, 0, 0)
            };
        }

        [TestMethod]
        public async Task LeerPicking_DevuelveElRecorridoOrdenado()
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, PICKING)).Returns(new List<LineaPickingAlmacenDTO>
            {
                new LineaPickingAlmacenDTO { Producto = "B", Pasillo = "003", Fila = "001", Columna = "010", Cantidad = 2 },
                new LineaPickingAlmacenDTO { Producto = "A", Pasillo = "001", Fila = "005", Columna = "016", Cantidad = 1 }
            });

            PickingAlmacenDTO picking = await servicio.LeerPicking(EMPRESA, PICKING);

            CollectionAssert.AreEqual(new[] { "A", "B" }, picking.Lineas.Select(l => l.Producto).ToList());
            Assert.AreEqual("001/005/016", picking.Lineas[0].Ubicacion);
        }

        [TestMethod]
        public void MontarPacking_DosPedidosDelMismoClienteYDireccion_VanEnLaMismaEntrega()
        {
            // Como el packing list de hoy: se agrupa por cliente y dirección de entrega
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[]
            {
                Fila(926940, 1, "A", "111", 2),
                Fila(926941, 5, "B", "222", 1),
                Fila(926950, 9, "C", "333", 1, cliente: "34384")
            });

            Assert.AreEqual(2, packing.Entregas.Count);
            EntregaPackingAlmacenDTO entrega = packing.Entregas.Single(e => e.Cliente == "40182");
            CollectionAssert.AreEqual(new[] { 926940, 926941 }, entrega.Pedidos.Select(p => p.Pedido).ToList());
            Assert.AreEqual("00", entrega.Ruta);
        }

        [TestMethod]
        public void MontarPacking_MarcaSinCodigoYCodigoDuplicadoDentroDeLaEntrega()
        {
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[]
            {
                Fila(926940, 1, "LIMA", " ", 5),
                Fila(926940, 2, "A", "111", 1),
                Fila(926941, 3, "B", "111", 1)
            });

            List<LineaPackingAlmacenDTO> lineas = packing.Entregas.Single().Pedidos.SelectMany(p => p.Lineas).ToList();
            Assert.IsTrue(lineas.Single(l => l.Producto == "LIMA").SinCodigo);
            Assert.IsTrue(lineas.Single(l => l.Producto == "A").CodigoDuplicado);
            Assert.IsTrue(lineas.Single(l => l.Producto == "B").CodigoDuplicado);
        }

        [TestMethod]
        public async Task LeerPackingDePedido_SinPickingEnCurso_Null()
        {
            A.CallTo(() => repositorio.PickingEnCursoDelPedido(EMPRESA, PEDIDO)).Returns(Task.FromResult<int?>(null));

            Assert.IsNull(await servicio.LeerPackingDePedido(EMPRESA, PEDIDO));
        }

        [TestMethod]
        public async Task GuardarEscaneos_GuardaLosNuevosYCuentaLosRepetidos()
        {
            EscaneoAlmacenDTO nuevo = Escaneo();
            EscaneoAlmacenDTO yaEstaba = Escaneo();
            A.CallTo(() => repositorio.InsertarEscaneo(EMPRESA, nuevo, A<string>._)).Returns(true);
            A.CallTo(() => repositorio.InsertarEscaneo(EMPRESA, yaEstaba, A<string>._)).Returns(false);

            ResultadoEscaneosAlmacenDTO resultado = await servicio.GuardarEscaneos(EMPRESA, new[] { nuevo, yaEstaba }, "Andrey");

            Assert.AreEqual(1, resultado.Guardados);
            Assert.AreEqual(1, resultado.Repetidos);
            Assert.AreEqual(0, resultado.Rechazados.Count);
        }

        [TestMethod]
        public async Task GuardarEscaneos_ElMismoEscaneoDosVecesEnElLote_SoloSeGuardaUno()
        {
            Guid id = Guid.NewGuid();
            A.CallTo(() => repositorio.InsertarEscaneo(EMPRESA, A<EscaneoAlmacenDTO>._, A<string>._)).Returns(true);

            ResultadoEscaneosAlmacenDTO resultado = await servicio.GuardarEscaneos(EMPRESA, new[] { Escaneo(id), Escaneo(id) }, "Andrey");

            Assert.AreEqual(1, resultado.Guardados);
            Assert.AreEqual(1, resultado.Repetidos);
            A.CallTo(() => repositorio.InsertarEscaneo(EMPRESA, A<EscaneoAlmacenDTO>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GuardarEscaneos_UnoMalo_NoTiraElLote()
        {
            EscaneoAlmacenDTO malo = Escaneo();
            malo.Fase = "OTRA";
            A.CallTo(() => repositorio.InsertarEscaneo(EMPRESA, A<EscaneoAlmacenDTO>._, A<string>._)).Returns(true);

            ResultadoEscaneosAlmacenDTO resultado = await servicio.GuardarEscaneos(EMPRESA, new[] { Escaneo(), malo, Escaneo() }, "Andrey");

            Assert.AreEqual(2, resultado.Guardados);
            Assert.AreEqual(malo.IdCliente, resultado.Rechazados.Single().IdCliente);
        }

        [TestMethod]
        public async Task GuardarEscaneos_SinUsuario_NoDejaLaAuditoriaVacia()
        {
            A.CallTo(() => repositorio.InsertarEscaneo(EMPRESA, A<EscaneoAlmacenDTO>._, A<string>._)).Returns(true);

            _ = await servicio.GuardarEscaneos(EMPRESA, new[] { Escaneo() }, null);

            A.CallTo(() => repositorio.InsertarEscaneo(EMPRESA, A<EscaneoAlmacenDTO>._,
                NestoAPI.Infraestructure.UsuarioAuditoriaHelper.DESCONOCIDO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GuardarEscaneos_LoteDesproporcionado_SeRechazaEntero()
        {
            List<EscaneoAlmacenDTO> lote = Enumerable.Range(0, ServicioPreparacionAlmacen.MAXIMO_ESCANEOS_POR_LOTE + 1)
                .Select(i => Escaneo()).ToList();

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.GuardarEscaneos(EMPRESA, lote, "Andrey"));
        }

        [TestMethod]
        public async Task GuardarFotoBulto_SubeLaFotoYGuardaLaFilaConSuRutaYSuHash()
        {
            FotoBultoAlmacen foto = Foto();
            A.CallTo(() => repositorio.GuardarBulto(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._))
                .ReturnsLazily((BultoAlmacenDTO b, string h, int t, string d) => Task.FromResult(b));

            BultoAlmacenDTO guardado = await servicio.GuardarFotoBulto(foto, "Andrey");

            const string rutaEsperada = "1/926940/99633/bulto-1-20260930120000.jpg";
            Assert.AreEqual(rutaEsperada, guardado.RutaBlob);
            A.CallTo(() => fotos.Subir(rutaEsperada, JPEG, "image/jpeg")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.GuardarBulto(A<BultoAlmacenDTO>._, ServicioPreparacionAlmacen.HashSha256(JPEG), JPEG.Length, null))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GuardarFotoBulto_ReenvioDeLaCola_NoVuelveASubirLaFoto()
        {
            FotoBultoAlmacen foto = Foto();
            var yaGuardado = new BultoAlmacenDTO { Id = 7, IdCliente = foto.IdCliente };
            A.CallTo(() => repositorio.LeerBultoPorIdCliente(foto.IdCliente)).Returns(yaGuardado);

            BultoAlmacenDTO resultado = await servicio.GuardarFotoBulto(foto, "Andrey");

            Assert.AreSame(yaGuardado, resultado);
            A.CallTo(() => fotos.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GuardarFotoBulto_SiLaSubidaFalla_NoQuedaFilaSinFoto()
        {
            A.CallTo(() => fotos.Subir(A<string>._, A<byte[]>._, A<string>._)).Throws(new InvalidOperationException("403"));

            _ = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => servicio.GuardarFotoBulto(Foto(), "Andrey"));

            A.CallTo(() => repositorio.GuardarBulto(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GuardarFotoBulto_PedidoQueNoEstaEnEsePicking_SeRechaza()
        {
            FotoBultoAlmacen foto = Foto();
            foto.Picking = 1;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => servicio.GuardarFotoBulto(foto, "Andrey"));

            StringAssert.Contains(ex.Message, "no está en el picking");
        }

        [TestMethod]
        public async Task GuardarFotoBulto_SinAlmacenamientoConfigurado_LoDiceYNoGuardaNada()
        {
            A.CallTo(() => fotos.Configurado).Returns(false);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => servicio.GuardarFotoBulto(Foto(), "Andrey"));

            Assert.AreEqual(System.Net.HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            A.CallTo(() => repositorio.GuardarBulto(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void MotivoDeRechazoDeLaFoto_SoloAdmiteJpegDeTamanoRazonable()
        {
            FotoBultoAlmacen vacia = Foto(); vacia.Imagen = new byte[0];
            FotoBultoAlmacen png = Foto(); png.Imagen = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            FotoBultoAlmacen enorme = Foto(); enorme.Imagen = new byte[ServicioPreparacionAlmacen.TAMANO_MAXIMO_FOTO + 1];
            FotoBultoAlmacen sinBulto = Foto(); sinBulto.Bulto = 0;

            Assert.IsNull(ServicioPreparacionAlmacen.MotivoDeRechazoDeLaFoto(Foto()));
            StringAssert.Contains(ServicioPreparacionAlmacen.MotivoDeRechazoDeLaFoto(vacia), "vacía");
            StringAssert.Contains(ServicioPreparacionAlmacen.MotivoDeRechazoDeLaFoto(png), "JPEG");
            StringAssert.Contains(ServicioPreparacionAlmacen.MotivoDeRechazoDeLaFoto(enorme), "máximo");
            StringAssert.Contains(ServicioPreparacionAlmacen.MotivoDeRechazoDeLaFoto(sinBulto), "bulto");
        }

        [TestMethod]
        public async Task EnlaceFotoBulto_BultoConFoto_DevuelveUnEnlaceDeQuinceMinutos()
        {
            var enlace = new Uri("https://cuenta.blob.core.windows.net/bultos/x.jpg?sig=abc");
            A.CallTo(() => repositorio.LeerBulto(7)).Returns(new BultoAlmacenDTO { Id = 7, RutaBlob = "x.jpg " });
            A.CallTo(() => fotos.EnlaceDeLectura("x.jpg", TimeSpan.FromMinutes(15))).Returns(enlace);

            Assert.AreEqual(enlace, await servicio.EnlaceFotoBulto(7));
        }

        [TestMethod]
        public async Task EnlaceFotoBulto_BultoInexistenteOSinFoto_Null()
        {
            A.CallTo(() => repositorio.LeerBulto(7)).Returns(Task.FromResult<BultoAlmacenDTO>(null));
            A.CallTo(() => repositorio.LeerBulto(8)).Returns(new BultoAlmacenDTO { Id = 8, RutaBlob = null });

            Assert.IsNull(await servicio.EnlaceFotoBulto(7));
            Assert.IsNull(await servicio.EnlaceFotoBulto(8));
        }

        [TestMethod]
        public async Task LeerEstadoPedido_TodoLeidoYTodosLosBultosConFoto_Completo()
        {
            A.CallTo(() => repositorio.LeerLineasPacking(EMPRESA, PICKING, PEDIDO)).Returns(new List<FilaPackingAlmacen>
            {
                Fila(PEDIDO, 1, "A", "111", 2), Fila(PEDIDO, 2, "B", "222", 1)
            });
            A.CallTo(() => repositorio.LeerLecturas(EMPRESA, PEDIDO, PICKING, "PACK")).Returns(new List<LecturaProductoAlmacen>
            {
                new LecturaProductoAlmacen { Producto = "A", Unidades = 2 }, new LecturaProductoAlmacen { Producto = "B", Unidades = 1 }
            });
            A.CallTo(() => repositorio.LeerBultos(EMPRESA, PEDIDO)).Returns(new List<BultoAlmacenDTO>
            {
                new BultoAlmacenDTO { Picking = PICKING, Bulto = 1, TieneFoto = true },
                new BultoAlmacenDTO { Picking = 1, Bulto = 1, TieneFoto = false } // de una entrega anterior: no cuenta
            });

            EstadoPreparacionPedidoDTO estado = await servicio.LeerEstadoPedido(EMPRESA, PEDIDO, PICKING);

            Assert.IsTrue(estado.Completo);
            Assert.IsTrue(estado.TodosLosBultosConFoto);
            Assert.AreEqual(1, estado.Bultos.Count);
        }

        [TestMethod]
        public async Task LeerEstadoPedido_FaltaUnProductoYNoHayBultos_NoCompletoNiConFoto()
        {
            A.CallTo(() => repositorio.PickingEnCursoDelPedido(EMPRESA, PEDIDO)).Returns(Task.FromResult<int?>(PICKING));
            A.CallTo(() => repositorio.LeerLineasPacking(EMPRESA, PICKING, PEDIDO)).Returns(new List<FilaPackingAlmacen>
            {
                Fila(PEDIDO, 1, "A", "111", 2)
            });
            A.CallTo(() => repositorio.LeerLecturas(EMPRESA, PEDIDO, PICKING, "PACK")).Returns(new List<LecturaProductoAlmacen>());
            A.CallTo(() => repositorio.LeerBultos(EMPRESA, PEDIDO)).Returns(new List<BultoAlmacenDTO>());

            EstadoPreparacionPedidoDTO estado = await servicio.LeerEstadoPedido(EMPRESA, PEDIDO, null);

            Assert.AreEqual(PICKING, estado.Picking);
            Assert.IsFalse(estado.Completo);
            Assert.AreEqual(-2, estado.Productos.Single().Diferencia);
            Assert.IsFalse(estado.TodosLosBultosConFoto);
        }
    }
}
