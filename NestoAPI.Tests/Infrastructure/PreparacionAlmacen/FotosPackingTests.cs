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
    /// NestoAPI#771 (Carlos, 31/10/26): las fotos del packing dejan de ir ligadas a los bultos. Mínimo 1 foto por entrega, las que
    /// quieran, sin relación con el número de cajas; las cajas se pueden cerrar sin foto; las de antes (una por caja) siguen valiendo.
    /// </summary>
    [TestClass]
    public class FotosPackingTests
    {
        private const string EMPRESA = "1";
        private const int PICKING = 99633;
        private const int PEDIDO = 926940;
        private const int OTRO_PEDIDO = 926941;
        private const string CLAVE = "clave-de-pruebas-de-los-enlaces-de-las-fotos-771";
        private static readonly byte[] JPEG = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

        private IRepositorioPreparacionAlmacen repositorio;
        private IAlmacenFotosBultos fotos;
        private ServicioPreparacionAlmacen servicio;
        private readonly List<string> subidas = new List<string>();
        private readonly List<FotoPackingAlmacenDTO> guardadas = new List<FotoPackingAlmacenDTO>();

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            fotos = A.Fake<IAlmacenFotosBultos>();
            A.CallTo(() => fotos.Configurado).Returns(true);
            A.CallTo(() => fotos.Subir(A<string>._, A<byte[]>._, A<string>._))
                .ReturnsLazily((string ruta, byte[] b, string t) => { subidas.Add(ruta); return Task.CompletedTask; });
            A.CallTo(() => fotos.EnlaceDeLectura(A<string>._, A<TimeSpan>._)).ReturnsLazily((string ruta, TimeSpan v) => new Uri("https://almacen.example/" + ruta));
            A.CallTo(() => repositorio.ExisteTablaFotosPacking()).Returns(true);
            A.CallTo(() => repositorio.LeerBultoPorIdCliente(A<Guid>._)).Returns(Task.FromResult<BultoAlmacenDTO>(null));
            A.CallTo(() => repositorio.ExistePedidoEnPicking(EMPRESA, A<int>._, PICKING)).Returns(true);
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._))
                .ReturnsLazily((BultoAlmacenDTO b, string h, int t, string d) => Task.FromResult(b));
            A.CallTo(() => repositorio.LeerFotoPackingPorIdCliente(A<Guid>._))
                .ReturnsLazily((Guid id) => Task.FromResult(guardadas.FirstOrDefault(f => f.IdCliente == id)));
            A.CallTo(() => repositorio.GuardarFotoPacking(A<FotoPackingAlmacenDTO>._, A<string>._, A<int>._, A<string>._))
                .ReturnsLazily((FotoPackingAlmacenDTO f, string h, int t, string d) =>
                {
                    f.Id = guardadas.Count + 1;
                    guardadas.Add(f);
                    return Task.FromResult(f);
                });
            servicio = new ServicioPreparacionAlmacen(repositorio, fotos, CLAVE) { RetrasoReintentoBultoMs = 0 };
        }

        private static FotoBultoAlmacen Foto(Guid? id = null, List<int> otros = null, byte[] imagen = null) => new FotoBultoAlmacen
        {
            IdCliente = id ?? Guid.NewGuid(),
            Empresa = EMPRESA,
            Pedido = PEDIDO,
            OtrosPedidos = otros,
            Picking = PICKING,
            Imagen = imagen ?? JPEG,
            FechaFoto = new DateTime(2026, 10, 31, 10, 0, 0),
            Dispositivo = "PDA-1"
        };

        // ===================================================================== foto libre del packing

        [TestMethod]
        public async Task FotoPacking_SinBulto_SeSubeYSeGuarda_ConSuEnlacePublico()
        {
            FotoBultoAlmacen foto = Foto();

            FotoPackingAlmacenDTO guardada = await servicio.GuardarFotoPacking(foto, "NUEVAVISION\\Andre");

            CollectionAssert.AreEqual(new[] { $"1/926940/99633/packing-{foto.IdCliente:N}.jpg" }, subidas);
            Assert.AreEqual(PEDIDO, guardada.Pedido);
            Assert.AreEqual(PICKING, guardada.Picking);
            Assert.IsTrue(guardada.TieneFoto);
            Assert.AreEqual(EnlacePublicoFotoBulto.RutaFotoPacking(CLAVE, guardada.Id, foto.IdCliente), guardada.RutaFotoPublica);
            StringAssert.StartsWith(guardada.RutaFotoPublica, "api/Almacen/Fotos/p");
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FotoPacking_LasQueSeQuiera_ElReenvioNoDuplica()
        {
            Guid id = Guid.NewGuid();
            _ = await servicio.GuardarFotoPacking(Foto(id), "Andre");
            _ = await servicio.GuardarFotoPacking(Foto(id), "Andre"); // la cola reenvía
            _ = await servicio.GuardarFotoPacking(Foto(), "Andre");
            _ = await servicio.GuardarFotoPacking(Foto(), "Andre");

            Assert.AreEqual(3, guardadas.Count, "tres fotos distintas, el reenvío no cuenta");
            Assert.AreEqual(3, subidas.Count);
        }

        [TestMethod]
        public async Task FotoPacking_OtrosPedidosDeLaEntrega_UnaFilaPorPedidoConLaMismaFoto()
        {
            FotoBultoAlmacen foto = Foto(otros: new List<int> { OTRO_PEDIDO, PEDIDO, 0 });

            _ = await servicio.GuardarFotoPacking(foto, "Andre");

            Assert.AreEqual(1, subidas.Count, "la foto se sube una vez");
            CollectionAssert.AreEqual(new[] { PEDIDO, OTRO_PEDIDO }, guardadas.Select(g => g.Pedido).ToList());
            Assert.AreEqual(guardadas[0].RutaBlob, guardadas[1].RutaBlob);
            Assert.AreEqual(ServicioPreparacionAlmacen.IdParaOtroPedido(foto.IdCliente, OTRO_PEDIDO), guardadas[1].IdCliente);
        }

        [TestMethod]
        public async Task FotoPacking_QueNoEsJpeg_OVacia_OPedidoFueraDelPicking_SeRechaza()
        {
            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.GuardarFotoPacking(Foto(imagen: new byte[] { 1, 2, 3 }), "Andre"));
            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.GuardarFotoPacking(Foto(imagen: new byte[0]), "Andre"));
            A.CallTo(() => repositorio.ExistePedidoEnPicking(EMPRESA, OTRO_PEDIDO, PICKING)).Returns(false);
            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.GuardarFotoPacking(Foto(otros: new List<int> { OTRO_PEDIDO }), "Andre"));
            Assert.AreEqual(0, subidas.Count, "nada se sube si algo no vale");
        }

        [TestMethod]
        public async Task FotoPacking_SinElScript_ErrorClaroYNadaSubido()
        {
            A.CallTo(() => repositorio.ExisteTablaFotosPacking()).Returns(false);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.GuardarFotoPacking(Foto(), "Andre"));

            StringAssert.Contains(ex.Message, "Issue771");
            Assert.AreEqual(0, subidas.Count);
        }

        // ===================================================================== cerrar caja sin foto

        [TestMethod]
        public async Task CerrarCajaSinFoto_NoSubeNadaYGuardaElBultoSinRuta()
        {
            var caja = new FotoBultoAlmacen { IdCliente = Guid.NewGuid(), Empresa = EMPRESA, Pedido = PEDIDO, Picking = PICKING, Bulto = 2, Dispositivo = "PDA-1" };

            BultoAlmacenDTO bulto = await servicio.CerrarBultoSinFoto(caja, "Andre");

            Assert.AreEqual(0, subidas.Count);
            Assert.IsNull(bulto.RutaBlob);
            Assert.IsNull(bulto.RutaFotoPublica);
            Assert.AreEqual(2, bulto.Bulto);
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>.That.Matches(b => b.RutaBlob == null && b.Bulto == 2), null, 0, "PDA-1"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CerrarCajaSinFoto_ConOtrosPedidos_UnaFilaPorPedido()
        {
            var caja = new FotoBultoAlmacen { IdCliente = Guid.NewGuid(), Empresa = EMPRESA, Pedido = PEDIDO, OtrosPedidos = new List<int> { OTRO_PEDIDO }, Picking = PICKING, Bulto = 1 };

            _ = await servicio.CerrarBultoSinFoto(caja, "Andre");

            A.CallTo(() => repositorio.GuardarBulto(A<BultoAlmacenDTO>.That.Matches(b => b.Pedido == OTRO_PEDIDO && b.RutaBlob == null && b.Bulto == 1), null, 0, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CerrarCajaSinFoto_SinNumeroDeBulto_SeRechaza()
        {
            var caja = new FotoBultoAlmacen { IdCliente = Guid.NewGuid(), Empresa = EMPRESA, Pedido = PEDIDO, Picking = PICKING, Bulto = 0 };

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.CerrarBultoSinFoto(caja, "Andre"));
        }

        [TestMethod]
        public async Task FotoDeUnBulto_ComoAntes_SigueExigiendoLaImagen()
        {
            var caja = new FotoBultoAlmacen { IdCliente = Guid.NewGuid(), Empresa = EMPRESA, Pedido = PEDIDO, Picking = PICKING, Bulto = 1 };

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.GuardarFotoBulto(caja, "Andre"));
        }

        [TestMethod]
        public void SqlDelBulto_CerrarSinFoto_NuncaBorraLaFotoQueYaTenia()
        {
            foreach (string sql in new[] { RepositorioPreparacionAlmacen.SQL_ACTUALIZAR_BULTO, RepositorioPreparacionAlmacen.SQL_GUARDAR_BULTO_SIN_PISAR })
            {
                StringAssert.Contains(sql, "RutaBlob = ISNULL(@p5, RutaBlob)");
                StringAssert.Contains(sql, "HashSha256 = CASE WHEN @p5 IS NULL THEN HashSha256 ELSE @p6 END");
            }
        }

        // ===================================================================== la regla: mínimo una foto por entrega

        private static EntregaPorEmpaquetar Entrega(int conFoto, int sinFoto, int fotosPacking)
            => new EntregaPorEmpaquetar { Picking = PICKING, Cliente = "1", Contacto = "0", Pedidos = 1, PedidosConEtiqueta = 0, BultosConFoto = conFoto, BultosSinFoto = sinFoto, FotosPacking = fotosPacking };

        [TestMethod]
        public void EntregaEmpaquetada_MinimoUnaFotoPorEntrega_SinMirarCuantasCajas()
        {
            Assert.IsTrue(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(0, 3, 1)), "tres cajas sin foto y una foto del packing");
            Assert.IsTrue(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(1, 0, 0)), "una caja con su foto, como antes");
            Assert.IsTrue(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(1, 2, 0)), "una foto basta aunque haya cajas sin ella");
            Assert.IsTrue(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(0, 1, 4)), "las fotos que quieran");
            Assert.IsFalse(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(0, 2, 0)), "cajas sin ninguna foto: no");
            Assert.IsFalse(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(0, 0, 2)), "fotos sin ninguna caja: no");
        }

        [TestMethod]
        public void SqlEntregasPorEmpaquetar_CuentaLasFotosDelPacking_SoloSiHayScript()
        {
            string con = RepositorioPreparacionAlmacen.SqlEntregasPorEmpaquetar(true);
            string sin = RepositorioPreparacionAlmacen.SqlEntregasPorEmpaquetar(false);

            StringAssert.Contains(con, "FROM dbo.PreparacionFotosPacking f");
            StringAssert.Contains(con, "AS FotosPacking");
            Assert.IsFalse(sin.Contains("PreparacionFotosPacking"), "sin el script la consulta no puede nombrar la tabla");
            StringAssert.Contains(sin, "/*FOTOS_PACKING*/0 AS FotosPacking");
        }

        [TestMethod]
        public void SqlFotosPacking_SinLaTabla_DevuelveVacioConTodasLasColumnas()
        {
            foreach (string sql in new[] { RepositorioPreparacionAlmacen.SQL_FOTOS_PACKING_DEL_PEDIDO, RepositorioPreparacionAlmacen.SQL_FOTOS_PACKING_DEL_PICKING,
                RepositorioPreparacionAlmacen.SQL_FOTO_PACKING_POR_ID, RepositorioPreparacionAlmacen.SQL_FOTO_PACKING_POR_ID_CLIENTE })
            {
                StringAssert.StartsWith(sql, "IF OBJECT_ID('dbo.PreparacionFotosPacking') IS NULL");
                StringAssert.Contains(sql, "SELECT TOP 0");
                foreach (string columna in new[] { "AS Id", "AS IdCliente", "AS Empresa", "AS Pedido", "AS Picking", "AS TieneFoto", "AS Usuario", "AS FechaFoto", "AS FechaRegistro", "AS RutaBlob" })
                {
                    StringAssert.Contains(sql, columna);
                }
            }
        }

        [TestMethod]
        public void RepartirBultosYActividad_CadaEntregaSusFotos()
        {
            var packing = new PackingAlmacenDTO
            {
                Picking = PICKING,
                Entregas = new List<EntregaPackingAlmacenDTO>
                {
                    new EntregaPackingAlmacenDTO { Pedidos = new List<PedidoPackingAlmacenDTO> { new PedidoPackingAlmacenDTO { Pedido = PEDIDO } } },
                    new EntregaPackingAlmacenDTO { Pedidos = new List<PedidoPackingAlmacenDTO> { new PedidoPackingAlmacenDTO { Pedido = OTRO_PEDIDO } } }
                }
            };
            var fotosPacking = new List<FotoPackingAlmacenDTO>
            {
                new FotoPackingAlmacenDTO { Id = 1, Pedido = PEDIDO, Picking = PICKING },
                new FotoPackingAlmacenDTO { Id = 2, Pedido = PEDIDO, Picking = PICKING },
                new FotoPackingAlmacenDTO { Id = 3, Pedido = OTRO_PEDIDO, Picking = PICKING },
                new FotoPackingAlmacenDTO { Id = 4, Pedido = PEDIDO, Picking = 1 } // de otro picking
            };

            ServicioPreparacionAlmacen.RepartirBultosYActividad(packing, null, null, fotosPacking);

            CollectionAssert.AreEqual(new[] { 1, 2 }, packing.Entregas[0].Fotos.Select(f => f.Id).ToList());
            CollectionAssert.AreEqual(new[] { 3 }, packing.Entregas[1].Fotos.Select(f => f.Id).ToList());
        }

        [TestMethod]
        public async Task EstadoDelPedido_TieneAlgunaFoto_ConUnaDelPacking()
        {
            A.CallTo(() => repositorio.LeerBultos(EMPRESA, PEDIDO)).Returns(new List<BultoAlmacenDTO>
            {
                new BultoAlmacenDTO { Id = 1, Pedido = PEDIDO, Picking = PICKING, Bulto = 1, TieneFoto = false }
            });
            A.CallTo(() => repositorio.LeerFotosPacking(EMPRESA, PEDIDO)).Returns(new List<FotoPackingAlmacenDTO>
            {
                new FotoPackingAlmacenDTO { Id = 9, Pedido = PEDIDO, Picking = PICKING, RutaBlob = "x.jpg", IdCliente = Guid.NewGuid() }
            });

            EstadoPreparacionPedidoDTO estado = await servicio.LeerEstadoPedido(EMPRESA, PEDIDO, PICKING);

            Assert.IsFalse(estado.TodosLosBultosConFoto);
            Assert.IsTrue(estado.TieneAlgunaFoto);
            Assert.AreEqual(1, estado.Fotos.Count);
            StringAssert.StartsWith(estado.Fotos[0].RutaFotoPublica, "api/Almacen/Fotos/p9-");
        }

        // ===================================================================== enlaces públicos

        [TestMethod]
        public async Task EnlacePublico_FotoDelPacking_SeAbreConSuToken_YNoConElDeUnBulto()
        {
            var id = Guid.NewGuid();
            A.CallTo(() => repositorio.LeerFotoPacking(9)).Returns(new FotoPackingAlmacenDTO { Id = 9, IdCliente = id, RutaBlob = "1/926940/99633/packing-a.jpg" });
            A.CallTo(() => repositorio.LeerBulto(9)).Returns(new BultoAlmacenDTO { Id = 9, IdCliente = id, RutaBlob = "1/926940/99633/bulto-a.jpg" });

            Uri packing = await servicio.EnlaceFotoBultoPublico(EnlacePublicoFotoBulto.TokenFotoPacking(CLAVE, 9, id));
            Uri bulto = await servicio.EnlaceFotoBultoPublico(EnlacePublicoFotoBulto.Token(CLAVE, 9, id));
            Uri cruzado = await servicio.EnlaceFotoBultoPublico("p" + EnlacePublicoFotoBulto.Token(CLAVE, 9, id));

            StringAssert.EndsWith(packing.AbsoluteUri, "packing-a.jpg");
            StringAssert.EndsWith(bulto.AbsoluteUri, "bulto-a.jpg", "los enlaces de los bultos de antes siguen igual");
            Assert.IsNull(cruzado, "la firma de un bulto no abre la foto del packing con el mismo número");
        }

        [TestMethod]
        public void TokenFotoPacking_DistintoDelDelBulto_YSeLee()
        {
            var id = Guid.NewGuid();
            string token = EnlacePublicoFotoBulto.TokenFotoPacking(CLAVE, 17, id);

            StringAssert.StartsWith(token, "p17-");
            Assert.IsTrue(EnlacePublicoFotoBulto.TryLeerIdFotoPacking(token, out int leido));
            Assert.AreEqual(17, leido);
            Assert.IsFalse(EnlacePublicoFotoBulto.TryLeerId(token, out _), "el lector de los bultos no lo toma por suyo");
            Assert.IsFalse(EnlacePublicoFotoBulto.TryLeerIdFotoPacking(EnlacePublicoFotoBulto.Token(CLAVE, 17, id), out _));
            Assert.IsTrue(EnlacePublicoFotoBulto.EsValidoFotoPacking(CLAVE, token, 17, id));
            Assert.IsFalse(EnlacePublicoFotoBulto.EsValidoFotoPacking(CLAVE, token, 17, Guid.NewGuid()));
            Assert.IsNull(EnlacePublicoFotoBulto.TokenFotoPacking(null, 17, id), "sin clave no hay enlace");
        }
    }
}
