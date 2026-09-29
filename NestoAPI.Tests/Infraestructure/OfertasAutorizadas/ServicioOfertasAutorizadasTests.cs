using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.OfertasAutorizadas;
using NestoAPI.Models;
using NestoAPI.Models.OfertasCombinadas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure.OfertasAutorizadas
{
    /// <summary>
    /// NestoAPI#233: informar a los vendedores de NestoApp de una oferta autorizada, SOLO bajo demanda
    /// (Nesto pregunta tras guardar), y lectura agregada de las vigentes para la pantalla de NestoApp#137.
    /// </summary>
    [TestClass]
    public class ServicioOfertasAutorizadasTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 29);

        private NVEntities db;
        private IServicioNotificacionesPush push;
        private DbSet<OfertaCombinada> fakeCombinadas;
        private DbSet<OfertaPermitida> fakePermitidas;
        private DbSet<OfertaEscalonada> fakeEscalonadas;
        private DbSet<Familia> fakeFamilias;
        private ServicioOfertasAutorizadas servicio;
        private NotificacionPushDTO enviada;
        private string aplicacionEnviada;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            push = A.Fake<IServicioNotificacionesPush>();
            fakeCombinadas = CrearFakeDbSet<OfertaCombinada>();
            fakePermitidas = CrearFakeDbSet<OfertaPermitida>();
            fakeEscalonadas = CrearFakeDbSet<OfertaEscalonada>();
            fakeFamilias = CrearFakeDbSet<Familia>();
            A.CallTo(() => db.OfertasCombinadas).Returns(fakeCombinadas);
            A.CallTo(() => db.OfertasPermitidas).Returns(fakePermitidas);
            A.CallTo(() => db.OfertasEscalonadas).Returns(fakeEscalonadas);
            A.CallTo(() => db.Familias).Returns(fakeFamilias);
            A.CallTo(() => fakeCombinadas.Include(A<string>.Ignored)).Returns(fakeCombinadas);
            A.CallTo(() => fakeEscalonadas.Include(A<string>.Ignored)).Returns(fakeEscalonadas);

            ConfigurarFakeDbSet(fakeCombinadas, new List<OfertaCombinada>());
            ConfigurarFakeDbSet(fakePermitidas, new List<OfertaPermitida>());
            ConfigurarFakeDbSet(fakeEscalonadas, new List<OfertaEscalonada>());
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia>());

            A.CallTo(() => push.EnviarATodosDeAplicacion(A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string aplicacion, NotificacionPushDTO notificacion) =>
                {
                    aplicacionEnviada = aplicacion;
                    enviada = notificacion;
                })
                .Returns(Task.FromResult(7));

            servicio = new ServicioOfertasAutorizadas(db, push) { Hoy = () => HOY };
        }

        #region Informar a los vendedores

        [TestMethod]
        public async Task InformarVendedores_Combinada_MandaLaPushATodosLosVendedoresDeNestoApp()
        {
            ConfigurarFakeDbSet(fakeCombinadas, new List<OfertaCombinada>
            {
                new OfertaCombinada { Id = 12, Empresa = "1  ", Nombre = "Kit Roseta ", ImporteMinimo = 120m, FechaHasta = new DateTime(2026, 10, 31) }
            });

            ResultadoInformarVendedoresDTO resultado = await servicio.InformarVendedores("combinada", 12, esNueva: true);

            Assert.AreEqual(Constantes.Aplicaciones.NESTO_APP, aplicacionEnviada);
            Assert.AreEqual("Nueva oferta autorizada", enviada.Titulo);
            Assert.AreEqual("Kit Roseta: desde 120,00 €. Válida hasta el 31/10/2026. Toca para ver el detalle.", enviada.Cuerpo);
            Assert.AreEqual("OfertaAutorizada", enviada.Tipo);
            Assert.AreEqual("/ofertas-autorizadas?tipo=combinada&id=12", enviada.Datos["ruta"]);
            Assert.AreEqual("combinada", enviada.Datos["tipo"]);
            Assert.AreEqual("12", enviada.Datos["id"]);
            Assert.AreEqual(7, resultado.DispositivosNotificados);
            Assert.AreEqual(enviada.Datos["ruta"], resultado.Ruta);
        }

        [TestMethod]
        public async Task InformarVendedores_CombinadaSinImporteNiFechaFin_SoloNombre()
        {
            ConfigurarFakeDbSet(fakeCombinadas, new List<OfertaCombinada>
            {
                new OfertaCombinada { Id = 3, Empresa = "1  ", Nombre = "3x2 en ceras", ImporteMinimo = 0 }
            });

            await servicio.InformarVendedores("combinada", 3, esNueva: false);

            Assert.AreEqual("Oferta actualizada", enviada.Titulo);
            Assert.AreEqual("3x2 en ceras. Toca para ver el detalle.", enviada.Cuerpo);
        }

        [TestMethod]
        public async Task InformarVendedores_TipoSinDistinguirMayusculas_UsaLaClaveCanonicaEnLaRuta()
        {
            ConfigurarFakeDbSet(fakeCombinadas, new List<OfertaCombinada>
            {
                new OfertaCombinada { Id = 3, Empresa = "1  ", Nombre = "X" }
            });

            await servicio.InformarVendedores("Combinada", 3, true);

            Assert.AreEqual("/ofertas-autorizadas?tipo=combinada&id=3", enviada.Datos["ruta"]);
        }

        [TestMethod]
        public async Task InformarVendedores_Familia_DescribeLaFamiliaYElNMasM()
        {
            ConfigurarFakeDbSet(fakePermitidas, new List<OfertaPermitida>
            {
                new OfertaPermitida { NºOrden = 55, Empresa = "1  ", Familia = "ROSETA    ", CantidadConPrecio = 6, CantidadRegalo = 2, FiltroProducto = "CERA ", SubGrupo = "cos" }
            });
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia>
            {
                new Familia { Empresa = "1  ", Número = "ROSETA    ", Descripción = "Roseta Cosmetics   " }
            });

            await servicio.InformarVendedores("familia", 55, true);

            Assert.AreEqual("Roseta Cosmetics: lleva 6 y te regalamos 2 (productos cuyo nombre empieza por «CERA», subgrupo COS). Toca para ver el detalle.", enviada.Cuerpo);
            Assert.AreEqual("/ofertas-autorizadas?tipo=familia&id=55", enviada.Datos["ruta"]);
        }

        [TestMethod]
        public async Task InformarVendedores_FamiliaDenegada_NoAvisaANadie()
        {
            ConfigurarFakeDbSet(fakePermitidas, new List<OfertaPermitida>
            {
                new OfertaPermitida { NºOrden = 56, Empresa = "1  ", Familia = "ROSETA", CantidadConPrecio = 6, CantidadRegalo = 2, Denegar = true }
            });

            await Assert.ThrowsExceptionAsync<OfertaNoAvisableException>(() => servicio.InformarVendedores("familia", 56, true));

            A.CallTo(() => push.EnviarATodosDeAplicacion(A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task InformarVendedores_Escalonada_DiceElMayorDescuento()
        {
            ConfigurarFakeDbSet(fakeEscalonadas, new List<OfertaEscalonada>
            {
                new OfertaEscalonada
                {
                    Id = 4, Empresa = "1  ", Nombre = "Tintes",
                    OfertasEscalonadasTramos = new List<OfertaEscalonadaTramo>
                    {
                        new OfertaEscalonadaTramo { CantidadMinima = 6, Descuento = 0.10m },
                        new OfertaEscalonadaTramo { CantidadMinima = 12, Descuento = 0.25m }
                    }
                }
            });

            await servicio.InformarVendedores("escalonada", 4, true);

            Assert.AreEqual("Tintes: descuentos por volumen de hasta el 25 %. Toca para ver los tramos.", enviada.Cuerpo);
            Assert.AreEqual("/ofertas-autorizadas?tipo=escalonada&id=4", enviada.Datos["ruta"]);
        }

        [TestMethod]
        public async Task InformarVendedores_OfertaInexistente_NoAvisaANadie()
        {
            await Assert.ThrowsExceptionAsync<OfertaAutorizadaNoEncontradaException>(() => servicio.InformarVendedores("combinada", 99, true));

            A.CallTo(() => push.EnviarATodosDeAplicacion(A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task InformarVendedores_TipoDesconocido_LanzaArgumentException()
        {
            Assert.IsFalse(servicio.EsTipoValido("campana"));
            Assert.IsFalse(servicio.EsTipoValido(null));
            Assert.IsTrue(servicio.EsTipoValido("ESCALONADA"));

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.InformarVendedores("campana", 1, true));
        }

        #endregion

        #region Lectura de las vigentes

        [TestMethod]
        public async Task LeerVigentes_SoloVigentesHoyYSinDenegaciones()
        {
            ConfigurarFakeDbSet(fakeCombinadas, new List<OfertaCombinada>
            {
                new OfertaCombinada { Id = 1, Empresa = "1  ", Nombre = "Vigente", FechaDesde = HOY.AddDays(-5) },
                new OfertaCombinada { Id = 2, Empresa = "1  ", Nombre = "Caducada", FechaHasta = HOY.AddDays(-1) },
                new OfertaCombinada { Id = 3, Empresa = "1  ", Nombre = "Futura", FechaDesde = HOY.AddDays(1) },
                new OfertaCombinada { Id = 4, Empresa = "3  ", Nombre = "Otra empresa" }
            });
            ConfigurarFakeDbSet(fakePermitidas, new List<OfertaPermitida>
            {
                new OfertaPermitida { NºOrden = 10, Empresa = "1  ", Familia = "ROSETA", CantidadConPrecio = 6, CantidadRegalo = 2 },
                new OfertaPermitida { NºOrden = 11, Empresa = "1  ", Familia = "ROSETA", CantidadConPrecio = 3, CantidadRegalo = 1, Denegar = true },
                new OfertaPermitida { NºOrden = 12, Empresa = "1  ", Familia = "ROSETA", CantidadConPrecio = 3, CantidadRegalo = 1, FechaHasta = HOY.AddDays(-1) },
                new OfertaPermitida { NºOrden = 13, Empresa = "1  ", Familia = "ROSETA", Cliente = "15191", CantidadConPrecio = 3, CantidadRegalo = 1 },
                new OfertaPermitida { NºOrden = 14, Empresa = "1  ", Número = "PROD1", CantidadConPrecio = 3, CantidadRegalo = 1 }
            });
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia>
            {
                new Familia { Empresa = "1  ", Número = "ROSETA", Descripción = "Roseta " }
            });
            ConfigurarFakeDbSet(fakeEscalonadas, new List<OfertaEscalonada>
            {
                new OfertaEscalonada { Id = 7, Empresa = "1  ", Nombre = "Tintes", FechaHasta = HOY },
                new OfertaEscalonada { Id = 8, Empresa = "1  ", Nombre = "Vieja", FechaHasta = HOY.AddDays(-1) }
            });

            OfertasAutorizadasDTO vigentes = await servicio.LeerVigentes("1");

            CollectionAssert.AreEqual(new[] { 1 }, vigentes.Combinadas.Select(o => o.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 10 }, vigentes.Familias.Select(o => o.NOrden).ToArray());
            Assert.AreEqual("Roseta", vigentes.Familias[0].FamiliaDescripcion);
            CollectionAssert.AreEqual(new[] { 7 }, vigentes.Escalonadas.Select(o => o.Id).ToArray());
            A.CallTo(() => push.EnviarATodosDeAplicacion(A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        #endregion

        #region Controller

        [TestMethod]
        public async Task Controller_InformarVendedores_TipoDesconocido_BadRequestSinLlamarAlServicio()
        {
            var fakeServicio = A.Fake<IServicioOfertasAutorizadas>();
            A.CallTo(() => fakeServicio.EsTipoValido("campana")).Returns(false);
            var controller = new OfertasAutorizadasController(fakeServicio);

            var resultado = await controller.InformarVendedores("campana", 1);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeServicio.InformarVendedores(A<string>._, A<int>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controller_InformarVendedores_OfertaInexistente_NotFound()
        {
            var fakeServicio = A.Fake<IServicioOfertasAutorizadas>();
            A.CallTo(() => fakeServicio.EsTipoValido(A<string>._)).Returns(true);
            A.CallTo(() => fakeServicio.InformarVendedores("combinada", 9, true)).ThrowsAsync(new OfertaAutorizadaNoEncontradaException("no"));
            var controller = new OfertasAutorizadasController(fakeServicio);

            var resultado = await controller.InformarVendedores("combinada", 9);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Controller_InformarVendedores_Denegacion_BadRequestConElMotivo()
        {
            var fakeServicio = A.Fake<IServicioOfertasAutorizadas>();
            A.CallTo(() => fakeServicio.EsTipoValido(A<string>._)).Returns(true);
            A.CallTo(() => fakeServicio.InformarVendedores("familia", 9, false)).ThrowsAsync(new OfertaNoAvisableException("Es una denegación"));
            var controller = new OfertasAutorizadasController(fakeServicio);

            var resultado = await controller.InformarVendedores("familia", 9, esNueva: false);

            Assert.AreEqual("Es una denegación", ((BadRequestErrorMessageResult)resultado).Message);
        }

        [TestMethod]
        public async Task Controller_GetOfertasAutorizadas_SinEmpresa_UsaLaEmpresaPorDefecto()
        {
            var fakeServicio = A.Fake<IServicioOfertasAutorizadas>();
            var dto = new OfertasAutorizadasDTO();
            A.CallTo(() => fakeServicio.LeerVigentes("1")).Returns(dto);
            var controller = new OfertasAutorizadasController(fakeServicio);

            var resultado = await controller.GetOfertasAutorizadas(null);

            Assert.AreSame(dto, ((OkNegotiatedContentResult<OfertasAutorizadasDTO>)resultado).Content);
        }

        [TestMethod]
        public void Controller_LlevaAuthorize()
        {
            Assert.IsTrue(typeof(OfertasAutorizadasController).GetCustomAttributes(typeof(System.Web.Http.AuthorizeAttribute), true).Any());
        }

        /// <summary>
        /// Decisión de Carlos (29/09/26): crear o modificar una oferta en las tres pestañas NO avisa a los
        /// vendedores; el aviso es una llamada aparte que Nesto solo hace si el usuario dice que sí. Si
        /// alguien engancha la push en el POST/PUT, estos controllers necesitarían el servicio de push (o
        /// el de ofertas autorizadas) y este test lo detecta.
        /// </summary>
        [TestMethod]
        public void CrearOModificarOfertas_NoPuedenMandarPush_LosControllersNoDependenDelAviso()
        {
            var prohibidos = new[] { typeof(IServicioNotificacionesPush), typeof(IServicioOfertasAutorizadas) };
            foreach (Type controller in new[] { typeof(OfertasCombinadasController), typeof(OfertasPermitidasFamiliaController), typeof(OfertasEscalonadasController) })
            {
                IEnumerable<Type> dependencias = controller.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
                    .Concat(controller.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType))
                    .Concat(controller.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Select(p => p.PropertyType));
                foreach (Type prohibido in prohibidos)
                {
                    Assert.IsFalse(dependencias.Any(d => prohibido.IsAssignableFrom(d)), $"{controller.Name} depende de {prohibido.Name}: guardar una oferta no debe avisar a los vendedores");
                }
            }
        }

        #endregion

        private static DbSet<T> CrearFakeDbSet<T>() where T : class
        {
            return A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, List<T> lista) where T : class
        {
            IQueryable<T> data = lista.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
