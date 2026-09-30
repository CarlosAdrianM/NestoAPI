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

        private static LineaPickingAlmacenDTO Parada(string producto, int cantidad, string columna = "001")
        {
            return new LineaPickingAlmacenDTO { Producto = producto, Cantidad = cantidad, Pasillo = "001", Fila = "001", Columna = columna };
        }

        private static LecturaPickingAlmacen Leido(string producto, int unidades, int faltas = 0)
        {
            return new LecturaPickingAlmacen { Producto = producto, Unidades = unidades, Faltas = faltas };
        }

        [TestMethod]
        public void MontarEstadoPicking_TodoCogido_TerminadoYCompleto()
        {
            // El producto A está en dos huecos: se mira por producto
            EstadoPickingDTO estado = ServicioPreparacionAlmacen.MontarEstadoPicking("1", PICKING,
                new[] { Parada("A", 2, "001"), Parada("A", 1, "002"), Parada("B", 1) },
                new[] { Leido("A", 3), Leido("B", 1) });

            Assert.IsTrue(estado.Terminado);
            Assert.IsTrue(estado.Completo);
        }

        [TestMethod]
        public void MontarEstadoPicking_UnaFaltaDeclarada_TerminadoPeroNoCompleto()
        {
            EstadoPickingDTO estado = ServicioPreparacionAlmacen.MontarEstadoPicking("1", PICKING,
                new[] { Parada("A", 3), Parada("B", 1) },
                new[] { Leido("A", 2, faltas: 1), Leido("B", 1) });

            Assert.IsTrue(estado.Terminado, "No queda nada por resolver");
            Assert.IsFalse(estado.Completo, "Pero ha faltado una unidad");
            Assert.AreEqual(1, estado.Productos.Single(p => p.Producto == "A").Faltas);
        }

        [TestMethod]
        public void MontarEstadoPicking_AMedias_NiTerminadoNiCompleto()
        {
            EstadoPickingDTO estado = ServicioPreparacionAlmacen.MontarEstadoPicking("1", PICKING,
                new[] { Parada("A", 3), Parada("B", 1) }, new[] { Leido("A", 3) });

            Assert.IsFalse(estado.Terminado);
            Assert.AreEqual(-1, estado.Productos.Single(p => p.Producto == "B").Diferencia);
        }

        [TestMethod]
        public async Task LeerEstadoPicking_PickingSinLineas_Null()
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 1)).Returns(new List<LineaPickingAlmacenDTO>());

            Assert.IsNull(await servicio.LeerEstadoPicking(EMPRESA, 1));
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
        public async Task GuardarFotoBulto_CajaCompartidaPorDosPedidos_UnaSubidaYUnaFilaPorPedido()
        {
            // Dos pedidos del mismo cliente para tener dos facturas, pero un solo bulto
            FotoBultoAlmacen foto = Foto();
            foto.OtrosPedidos = new List<int> { 926941, PEDIDO };
            A.CallTo(() => repositorio.ExistePedidoEnPicking(EMPRESA, 926941, PICKING)).Returns(true);
            var guardados = new List<BultoAlmacenDTO>();
            A.CallTo(() => repositorio.GuardarBulto(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._))
                .ReturnsLazily((BultoAlmacenDTO b, string h, int t, string d) => { guardados.Add(b); return Task.FromResult(b); });

            _ = await servicio.GuardarFotoBulto(foto, "Andrey");

            A.CallTo(() => fotos.Subir(A<string>._, A<byte[]>._, A<string>._)).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { PEDIDO, 926941 }, guardados.Select(b => b.Pedido).ToList());
            Assert.AreEqual(guardados[0].RutaBlob, guardados[1].RutaBlob, "La misma foto para los dos");
            Assert.AreNotEqual(guardados[0].IdCliente, guardados[1].IdCliente);
        }

        [TestMethod]
        public async Task GuardarFotoBulto_OtroPedidoQueNoEstaEnElPicking_NoSubeNada()
        {
            FotoBultoAlmacen foto = Foto();
            foto.OtrosPedidos = new List<int> { 1 };

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.GuardarFotoBulto(foto, "Andrey"));

            A.CallTo(() => fotos.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GuardarFotoBulto_ReenvioDeUnaCajaCompartidaQueSeQuedoAMedias_CompletaLaFilaQueFaltaba()
        {
            FotoBultoAlmacen foto = Foto();
            foto.OtrosPedidos = new List<int> { 926941 };
            var principal = new BultoAlmacenDTO { Id = 7, IdCliente = foto.IdCliente, Pedido = PEDIDO, RutaBlob = "1/926940/99633/bulto-1-x.jpg" };
            A.CallTo(() => repositorio.LeerBultoPorIdCliente(foto.IdCliente)).Returns(principal);

            _ = await servicio.GuardarFotoBulto(foto, "Andrey");

            A.CallTo(() => fotos.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarBulto(
                A<BultoAlmacenDTO>.That.Matches(b => b.Pedido == 926941 && b.RutaBlob == principal.RutaBlob),
                A<string>._, A<int>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void IdParaOtroPedido_EsSiempreElMismoParaLaMismaFotoYElMismoPedido()
        {
            Guid foto = Guid.NewGuid();

            Assert.AreEqual(ServicioPreparacionAlmacen.IdParaOtroPedido(foto, 926941), ServicioPreparacionAlmacen.IdParaOtroPedido(foto, 926941));
            Assert.AreNotEqual(ServicioPreparacionAlmacen.IdParaOtroPedido(foto, 926941), ServicioPreparacionAlmacen.IdParaOtroPedido(foto, 926942));
            Assert.AreNotEqual(foto, ServicioPreparacionAlmacen.IdParaOtroPedido(foto, 926941));
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

        #region NestoAPI#574: recoger es lo mismo para un picking y para una reposición

        private static LineaPickingAlmacenDTO Parada(int orden, string producto, int cantidad, string hueco)
        {
            return new LineaPickingAlmacenDTO
            {
                Orden = orden, Producto = producto, Descripcion = "PRODUCTO " + producto, CodigoBarras = "84" + producto,
                Cantidad = cantidad, Pasillo = hueco, Fila = "001", Columna = "001", Ubicacion = hueco + "/001/001"
            };
        }

        [TestMethod]
        public async Task LeerRecogidasPendientes_LosPickingsEnCursoSonRecogidasHaciaLaMesaDePacking()
        {
            A.CallTo(() => repositorio.LeerPickingsEnCurso(EMPRESA, "ALG")).Returns(new List<PickingEnCursoDTO>
            {
                new PickingEnCursoDTO { Picking = 99700, Lineas = 26, Pedidos = 7, Unidades = 80 }
            });

            RecogidaPendienteDTO recogida = (await servicio.LeerRecogidasPendientes(EMPRESA, "ALG")).Single();

            Assert.AreEqual("PICK", recogida.Tipo);
            Assert.AreEqual(99700, recogida.Numero);
            Assert.AreEqual("Mesa de packing", recogida.Destino);
            Assert.AreEqual(26, recogida.Lineas);
            Assert.AreEqual(7, recogida.Pedidos);
            Assert.AreEqual(80, recogida.Unidades);
        }

        [TestMethod]
        public void MontarRecogida_AMedias_AbrePorLaPrimeraParadaConAlgoPorCoger()
        {
            // El producto A está en dos huecos (3 + 2). Se han leído 4 de A y B se ha dado por falta.
            var recorrido = new List<LineaPickingAlmacenDTO>
            {
                Parada(1, "A", 3, "001"), Parada(2, "B", 1, "002"), Parada(3, "A", 2, "005"), Parada(4, "C", 6, "007")
            };
            var lecturas = new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A ", Unidades = 4 },
                new LecturaPickingAlmacen { Producto = "B", Faltas = 1 }
            };

            RecogidaAlmacenDTO recogida = ServicioPreparacionAlmacen.MontarRecogida(EMPRESA, "PICK", PICKING, "Mesa de packing", recorrido, lecturas);

            CollectionAssert.AreEqual(new[] { 3, 1, 1, 0 }, recogida.Lineas.Select(l => l.Resuelto).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 6 }, recogida.Lineas.Select(l => l.Pendiente).ToArray());
            Assert.AreEqual(3, recogida.SiguienteOrden);
            Assert.IsFalse(recogida.Terminada);
            Assert.AreEqual("005/001/001", recogida.Lineas[2].Ubicacion);
            Assert.AreEqual("84A", recogida.Lineas[2].CodigoBarras);
        }

        [TestMethod]
        public void MontarRecogida_TodoCogido_TerminadaYSinSiguienteParada()
        {
            var recorrido = new List<LineaPickingAlmacenDTO> { Parada(1, "A", 3, "001"), Parada(2, "B", 1, "002") };
            var lecturas = new List<LecturaPickingAlmacen>
            {
                // De A se han leído 5: lo de más no se apunta a ninguna parada
                new LecturaPickingAlmacen { Producto = "A", Unidades = 5 },
                new LecturaPickingAlmacen { Producto = "B", Unidades = 1 }
            };

            RecogidaAlmacenDTO recogida = ServicioPreparacionAlmacen.MontarRecogida(EMPRESA, "PICK", PICKING, "Mesa de packing", recorrido, lecturas);

            CollectionAssert.AreEqual(new[] { 3, 1 }, recogida.Lineas.Select(l => l.Resuelto).ToArray());
            Assert.IsNull(recogida.SiguienteOrden);
            Assert.IsTrue(recogida.Terminada);
            Assert.IsFalse(recogida.Completa, "Sobra producto: terminada, pero no completa");
        }

        [TestMethod]
        public void MontarRecogida_SinEmpezar_AbrePorLaPrimeraParada()
        {
            var recorrido = new List<LineaPickingAlmacenDTO> { Parada(1, "A", 3, "001"), Parada(2, "B", 1, "002") };

            RecogidaAlmacenDTO recogida = ServicioPreparacionAlmacen.MontarRecogida(EMPRESA, "PICK", PICKING, "Mesa de packing", recorrido, null);

            Assert.AreEqual(1, recogida.SiguienteOrden);
            Assert.IsTrue(recogida.Lineas.All(l => l.Resuelto == 0));
            Assert.IsFalse(recogida.Terminada);
        }

        [TestMethod]
        public async Task LeerRecogida_UnPicking_TraeElRecorridoConLoLeido()
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, PICKING)).Returns(new List<LineaPickingAlmacenDTO>
            {
                Parada(0, "B", 1, "002"), Parada(0, "A", 3, "001")
            });
            A.CallTo(() => repositorio.LeerLecturasDelPicking(EMPRESA, PICKING)).Returns(new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A", Unidades = 3 }
            });

            RecogidaAlmacenDTO recogida = await servicio.LeerRecogida(EMPRESA, " pick ", PICKING);

            Assert.AreEqual("PICK", recogida.Tipo);
            Assert.AreEqual(PICKING, recogida.Numero);
            Assert.AreEqual("A", recogida.Lineas[0].Producto, "Ordenado por el recorrido: el pasillo 001 antes que el 002");
            Assert.AreEqual(2, recogida.SiguienteOrden);
        }

        [TestMethod]
        public async Task LeerRecogida_PickingSinLineasOReposicion_Null()
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 1)).Returns(new List<LineaPickingAlmacenDTO>());

            Assert.IsNull(await servicio.LeerRecogida(EMPRESA, "PICK", 1));
            // La reposición todavía no tiene documento que leer (#553)
            Assert.IsNull(await servicio.LeerRecogida(EMPRESA, "REPO", 5012));
            Assert.IsNull(await servicio.LeerRecogida(EMPRESA, "OTRO", 5012));
        }

        #endregion

        #region Enlace público a la foto (sin usuario)

        private const string CLAVE_ENLACES = "clave-de-pruebas-de-mas-de-treinta-y-dos-caracteres";
        private static readonly Guid ID_FOTO = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

        [TestMethod]
        public async Task LeerBultos_ConClaveDeEnlaces_LosBultosConFotoLlevanSuEnlacePublico()
        {
            servicio = new ServicioPreparacionAlmacen(repositorio, fotos, CLAVE_ENLACES);
            A.CallTo(() => repositorio.LeerBultos(EMPRESA, PEDIDO)).Returns(new List<BultoAlmacenDTO>
            {
                new BultoAlmacenDTO { Id = 7, IdCliente = ID_FOTO, Bulto = 1, TieneFoto = true, RutaBlob = "x.jpg" },
                new BultoAlmacenDTO { Id = 8, IdCliente = Guid.NewGuid(), Bulto = 2, TieneFoto = false, RutaBlob = null }
            });

            List<BultoAlmacenDTO> bultos = await servicio.LeerBultos(EMPRESA, PEDIDO);

            Assert.AreEqual(EnlacePublicoFotoBulto.Ruta(CLAVE_ENLACES, 7, ID_FOTO), bultos[0].RutaFotoPublica);
            StringAssert.StartsWith(bultos[0].RutaFotoPublica, "api/Almacen/Fotos/7-");
            Assert.IsNull(bultos[1].RutaFotoPublica);
        }

        [TestMethod]
        public async Task LeerBultos_SinClaveDeEnlaces_NoHayEnlacePublico()
        {
            A.CallTo(() => repositorio.LeerBultos(EMPRESA, PEDIDO)).Returns(new List<BultoAlmacenDTO>
            {
                new BultoAlmacenDTO { Id = 7, IdCliente = ID_FOTO, Bulto = 1, TieneFoto = true, RutaBlob = "x.jpg" }
            });

            Assert.IsNull((await servicio.LeerBultos(EMPRESA, PEDIDO))[0].RutaFotoPublica);
        }

        [TestMethod]
        public async Task EnlaceFotoBultoPublico_ConElEnlaceBueno_DaAccesoDeQuinceMinutos()
        {
            servicio = new ServicioPreparacionAlmacen(repositorio, fotos, CLAVE_ENLACES);
            var enlace = new Uri("https://cuenta.blob.core.windows.net/bultos/x.jpg?sig=abc");
            A.CallTo(() => repositorio.LeerBulto(7)).Returns(new BultoAlmacenDTO { Id = 7, IdCliente = ID_FOTO, RutaBlob = "x.jpg " });
            A.CallTo(() => fotos.EnlaceDeLectura("x.jpg", TimeSpan.FromMinutes(15))).Returns(enlace);

            Assert.AreEqual(enlace, await servicio.EnlaceFotoBultoPublico(EnlacePublicoFotoBulto.Token(CLAVE_ENLACES, 7, ID_FOTO)));
        }

        [TestMethod]
        public async Task EnlaceFotoBultoPublico_EnlaceDeOtroBultoOInventado_Null()
        {
            servicio = new ServicioPreparacionAlmacen(repositorio, fotos, CLAVE_ENLACES);
            A.CallTo(() => repositorio.LeerBulto(7)).Returns(new BultoAlmacenDTO { Id = 7, IdCliente = ID_FOTO, RutaBlob = "x.jpg" });
            A.CallTo(() => repositorio.LeerBulto(8)).Returns(new BultoAlmacenDTO { Id = 8, IdCliente = Guid.NewGuid(), RutaBlob = "y.jpg" });
            A.CallTo(() => repositorio.LeerBulto(9)).Returns(Task.FromResult<BultoAlmacenDTO>(null));
            string bueno = EnlacePublicoFotoBulto.Token(CLAVE_ENLACES, 7, ID_FOTO);

            // El del 7 con el número cambiado al 8, uno de un bulto que no existe, y basura
            Assert.IsNull(await servicio.EnlaceFotoBultoPublico("8" + bueno.Substring(1)));
            Assert.IsNull(await servicio.EnlaceFotoBultoPublico("9" + bueno.Substring(1)));
            Assert.IsNull(await servicio.EnlaceFotoBultoPublico("7-" + new string('0', 40)));
            Assert.IsNull(await servicio.EnlaceFotoBultoPublico("lo-que-sea"));
            Assert.IsNull(await servicio.EnlaceFotoBultoPublico(null));
            A.CallTo(() => fotos.EnlaceDeLectura(A<string>._, A<TimeSpan>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnlaceFotoBultoPublico_SinClaveConfigurada_NoSeAbreNada()
        {
            A.CallTo(() => repositorio.LeerBulto(7)).Returns(new BultoAlmacenDTO { Id = 7, IdCliente = ID_FOTO, RutaBlob = "x.jpg" });

            Assert.IsNull(await servicio.EnlaceFotoBultoPublico(EnlacePublicoFotoBulto.Token(CLAVE_ENLACES, 7, ID_FOTO)));
            A.CallTo(() => repositorio.LeerBulto(A<int>._)).MustNotHaveHappened();
        }

        #endregion

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
