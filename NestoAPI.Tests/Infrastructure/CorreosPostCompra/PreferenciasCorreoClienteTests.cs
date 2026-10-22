using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>NestoAPI#532 (bajas por tipo): bajas de todo o de un tipo, y las preferencias de la app.</summary>
    [TestClass]
    public class PreferenciasCorreoClienteTests
    {
        private const string EMPRESA = "1";
        private const string CLIENTE = "15191";
        private const string EMAIL = "lidia@peluqueria.es";

        private IRepositorioPreferenciasCorreo repositorio;
        private BajasCorreosPostCompra bajas;
        private ServicioPreferenciasCorreo servicio;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioPreferenciasCorreo>();
            bajas = new BajasCorreosPostCompra();
            A.CallTo(() => repositorio.LeerBajas(EMPRESA, CLIENTE, EMAIL)).ReturnsLazily(() => Task.FromResult(bajas));
            // Lo que hace el SQL de verdad, para que la lista que se devuelve tras el PUT sea la de después
            A.CallTo(() => repositorio.DarDeBaja(EMPRESA, CLIENTE, A<string>._, A<string>._, A<string>._))
                .Invokes((string e, string c, string tipo, string m, string u) => bajas.Agregar(c, null, tipo))
                .Returns(Task.CompletedTask);
            A.CallTo(() => repositorio.QuitarBajas(EMPRESA, CLIENTE, EMAIL, A<string>._))
                .Invokes((string e, string c, string correo, string tipo) => bajas = SinFilas(bajas, tipo))
                .Returns(Task.CompletedTask);
            servicio = new ServicioPreferenciasCorreo(repositorio);
        }

        /// <summary>Simula el DELETE: rehace las bajas del cliente sin las del tipo (null = las de todo).</summary>
        private static BajasCorreosPostCompra SinFilas(BajasCorreosPostCompra antes, string tipo)
        {
            var despues = new BajasCorreosPostCompra();
            bool general = antes.Incluye(CLIENTE, EMAIL);
            if (general && tipo != null)
            {
                despues.Agregar(CLIENTE, null, null);
            }
            foreach (TipoCorreoCliente t in TiposCorreoCliente.Todos)
            {
                if (t.Tipo != tipo && antes.IncluyeSoloTipo(CLIENTE, EMAIL, t.Tipo))
                {
                    despues.Agregar(CLIENTE, null, t.Tipo);
                }
            }
            return despues;
        }

        private static bool Activo(List<PreferenciaCorreoDTO> lista, string tipo) => lista.Single(p => p.Tipo == tipo).Activo;

        [TestMethod]
        public void Bajas_LaGeneralQuitaTodo_LaDeUnTipoSoloEse()
        {
            var b = new BajasCorreosPostCompra();
            b.Agregar("1", null, null);
            b.Agregar("2", null, TiposCorreoCliente.RECORDATORIO_REPOSICION);
            b.Agregar(null, "Tres@Cliente.es ", TiposCorreoCliente.VIDEOS_COMPRAS);

            Assert.IsTrue(b.Incluye("1", null, TiposCorreoCliente.RECORDATORIO_REPOSICION));
            Assert.IsTrue(b.Incluye("1", null, TiposCorreoCliente.VIDEOS_COMPRAS));
            Assert.IsTrue(b.Incluye("2 ", null, TiposCorreoCliente.RECORDATORIO_REPOSICION));
            Assert.IsFalse(b.Incluye("2", null, TiposCorreoCliente.VIDEOS_COMPRAS));
            Assert.IsFalse(b.Incluye("2", null), "La baja de un tipo no es la de todo");
            Assert.IsTrue(b.Incluye("9", "tres@cliente.es", TiposCorreoCliente.VIDEOS_COMPRAS));
            Assert.IsFalse(b.Incluye("9", "tres@cliente.es", TiposCorreoCliente.RECORDATORIO_REPOSICION));
        }

        [TestMethod]
        public void TiposCorreoCliente_NombresDeUstedYConTildes()
        {
            CollectionAssert.AreEqual(new[] { "RecordatorioReposicion", "VideosCompras" }, TiposCorreoCliente.Todos.Select(t => t.Tipo).ToArray());
            Assert.AreEqual("Aviso de reposición", TiposCorreoCliente.Buscar("recordatorioreposicion").Nombre);
            Assert.AreEqual("Vídeos de sus compras", TiposCorreoCliente.Buscar(" VideosCompras ").Nombre);
            Assert.IsNull(TiposCorreoCliente.Buscar("Facturas"));
            Assert.IsNull(TiposCorreoCliente.Buscar(null));
            foreach (TipoCorreoCliente t in TiposCorreoCliente.Todos)
            {
                StringAssert.DoesNotMatch(t.Descripcion, new System.Text.RegularExpressions.Regex(@"\b(tu|tus|te|has)\b"), "De usted: " + t.Tipo);
            }
        }

        [TestMethod]
        public async Task Leer_SinBajas_TodoActivo()
        {
            List<PreferenciaCorreoDTO> lista = await servicio.Leer(EMPRESA, CLIENTE, EMAIL);

            Assert.AreEqual(TiposCorreoCliente.Todos.Count, lista.Count);
            Assert.IsTrue(lista.All(p => p.Activo));
            Assert.IsTrue(lista.All(p => !string.IsNullOrWhiteSpace(p.Nombre) && !string.IsNullOrWhiteSpace(p.Descripcion)));
        }

        [TestMethod]
        public async Task Leer_ConBajaDeTodoPorCorreo_TodoApagado()
        {
            bajas.Agregar(null, EMAIL, null);

            List<PreferenciaCorreoDTO> lista = await servicio.Leer(EMPRESA, CLIENTE, EMAIL);

            Assert.IsTrue(lista.All(p => !p.Activo));
        }

        [TestMethod]
        public async Task Cambiar_Apagar_DaDeBajaSoloEseTipo()
        {
            List<PreferenciaCorreoDTO> lista = await servicio.Cambiar(EMPRESA, CLIENTE, EMAIL, "recordatorioReposicion", false, EMAIL);

            A.CallTo(() => repositorio.DarDeBaja(EMPRESA, CLIENTE, TiposCorreoCliente.RECORDATORIO_REPOSICION,
                ServicioPreferenciasCorreo.MOTIVO_APP, EMAIL)).MustHaveHappenedOnceExactly();
            Assert.IsFalse(Activo(lista, TiposCorreoCliente.RECORDATORIO_REPOSICION));
            Assert.IsTrue(Activo(lista, TiposCorreoCliente.VIDEOS_COMPRAS));
        }

        [TestMethod]
        public async Task Cambiar_ApagarLoYaApagado_NoDuplica()
        {
            bajas.Agregar(CLIENTE, null, TiposCorreoCliente.RECORDATORIO_REPOSICION);

            _ = await servicio.Cambiar(EMPRESA, CLIENTE, EMAIL, TiposCorreoCliente.RECORDATORIO_REPOSICION, false, EMAIL);

            A.CallTo(() => repositorio.DarDeBaja(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cambiar_Encender_QuitaLaBajaDeEseTipo()
        {
            bajas.Agregar(CLIENTE, null, TiposCorreoCliente.RECORDATORIO_REPOSICION);
            bajas.Agregar(CLIENTE, null, TiposCorreoCliente.VIDEOS_COMPRAS);

            List<PreferenciaCorreoDTO> lista = await servicio.Cambiar(EMPRESA, CLIENTE, EMAIL, TiposCorreoCliente.RECORDATORIO_REPOSICION, true, EMAIL);

            A.CallTo(() => repositorio.QuitarBajas(EMPRESA, CLIENTE, EMAIL, TiposCorreoCliente.RECORDATORIO_REPOSICION)).MustHaveHappenedOnceExactly();
            Assert.IsTrue(Activo(lista, TiposCorreoCliente.RECORDATORIO_REPOSICION));
            Assert.IsFalse(Activo(lista, TiposCorreoCliente.VIDEOS_COMPRAS));
        }

        [TestMethod]
        public async Task Cambiar_EncenderUnoConBajaDeTodo_LosDemasSiguenApagados()
        {
            bajas.Agregar(CLIENTE, null, null);

            List<PreferenciaCorreoDTO> lista = await servicio.Cambiar(EMPRESA, CLIENTE, EMAIL, TiposCorreoCliente.RECORDATORIO_REPOSICION, true, EMAIL);

            A.CallTo(() => repositorio.DarDeBaja(EMPRESA, CLIENTE, TiposCorreoCliente.VIDEOS_COMPRAS, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => repositorio.QuitarBajas(EMPRESA, CLIENTE, EMAIL, null)).MustHaveHappened());
            Assert.IsTrue(Activo(lista, TiposCorreoCliente.RECORDATORIO_REPOSICION));
            Assert.IsFalse(Activo(lista, TiposCorreoCliente.VIDEOS_COMPRAS));
        }

        [TestMethod]
        public async Task Cambiar_TipoQueNoExiste_NullYNoToca()
        {
            Assert.IsNull(await servicio.Cambiar(EMPRESA, CLIENTE, EMAIL, "Facturas", false, EMAIL));
            A.CallTo(() => repositorio.DarDeBaja(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EstaDeBaja_SiNoSePuedenLeerLasBajas_SeEnviaComoAntes()
        {
            var roto = A.Fake<IRepositorioPreferenciasCorreo>();
            A.CallTo(() => roto.LeerBajas(A<string>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("Invalid column name 'Tipo'"));

            Assert.IsFalse(await ServicioPreferenciasCorreo.EstaDeBaja(roto, EMPRESA, CLIENTE, EMAIL, TiposCorreoCliente.VIDEOS_COMPRAS));
        }

        [TestMethod]
        public async Task EstaDeBaja_PorSuTipoOPorLaGeneral()
        {
            bajas.Agregar(CLIENTE, null, TiposCorreoCliente.RECORDATORIO_REPOSICION);
            Assert.IsFalse(await ServicioPreferenciasCorreo.EstaDeBaja(repositorio, EMPRESA, CLIENTE, EMAIL, TiposCorreoCliente.VIDEOS_COMPRAS));

            bajas.Agregar(null, EMAIL, null);
            Assert.IsTrue(await ServicioPreferenciasCorreo.EstaDeBaja(repositorio, EMPRESA, CLIENTE, EMAIL, TiposCorreoCliente.VIDEOS_COMPRAS));
        }

        [TestMethod]
        public void CorreoDeLosSabados_ElPieDiceQueLaBajaEsSoloDeEsosCorreos()
        {
            string pie = WebUtility.HtmlDecode(CorreosPostCompraJobsService.GenerarPieBaja());
            StringAssert.Contains(pie, "vídeos de sus compras");
            StringAssert.Contains(pie, "Es solo para estos correos");
        }

        [TestMethod]
        public void Recordatorio_LaFraseDeBajaDiceQueEsSoloDeEsteAviso()
        {
            StringAssert.Contains(PlantillaRecordatorioReposicion.FRASE_BAJA, "avisos de reposición");
            StringAssert.Contains(PlantillaRecordatorioReposicion.FRASE_BAJA, "Es solo para este aviso");
        }
    }
}
