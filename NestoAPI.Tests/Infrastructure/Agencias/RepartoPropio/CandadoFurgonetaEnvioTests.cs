using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Carlos 20/11/26 (envíos 251169 y 251227): en el reparto propio manda el bulto. Nada de oficina toca un envío de la 14 con bultos en
    /// una furgoneta (409 EN_FURGONETA), «Dar por cerrado» no vale para la 14 (CIERRE_NO_PERMITIDO), borrar deja siempre rastro y se
    /// pueden apuntar notas en la historia.
    /// </summary>
    [TestClass]
    public class CandadoFurgonetaEnvioTests
    {
        private static readonly DateTime HOY = DateTime.Today;

        private static MovimientoBultoReparto Mov(int id, string codigo, string tipo, DateTime cuando, string usuario = @"NUEVAVISION\Andre",
            int? envio = 251169, int? pedido = null, string tipoCodigo = CodigoBultoFurgoneta.RP)
            => new MovimientoBultoReparto { Id = id, Codigo = codigo, TipoCodigo = tipoCodigo, EnvioId = envio, Pedido = pedido, Tipo = tipo, Usuario = usuario, FechaHora = cuando };

        // ================================================================ LA REGLA

        [TestMethod]
        public void QuienLoLleva_UltimoCargado_LoLlevaAndreDesdeEsaHora()
        {
            var historia = new[]
            {
                Mov(1, "RP251169-1", TiposMovimientoBulto.CARGADO, HOY.AddHours(9).AddMinutes(42)),
                Mov(2, "RP251169-2", TiposMovimientoBulto.CARGADO, HOY.AddHours(9).AddMinutes(45)),
            };

            BultosEnFurgoneta f = CandadoFurgonetaEnvio.QuienLoLleva(251169, null, historia);

            Assert.IsNotNull(f);
            Assert.AreEqual("Andre", f.Repartidor);
            Assert.AreEqual(HOY.AddHours(9).AddMinutes(42), f.Desde);
            CollectionAssert.AreEquivalent(new[] { "RP251169-1", "RP251169-2" }, f.Codigos);
        }

        [TestMethod]
        public void QuienLoLleva_DescargadoOEntregado_Nadie()
        {
            var historia = new[]
            {
                Mov(1, "RP251169-1", TiposMovimientoBulto.CARGADO, HOY.AddHours(9)),
                Mov(2, "RP251169-1", TiposMovimientoBulto.DESCARGADO, HOY.AddHours(18)),
                Mov(3, "RP251169-2", TiposMovimientoBulto.CARGADO, HOY.AddHours(9)),
                Mov(4, "RP251169-2", TiposMovimientoBulto.ENTREGADO, HOY.AddHours(11)),
            };

            Assert.IsNull(CandadoFurgonetaEnvio.QuienLoLleva(251169, null, historia));
        }

        [TestMethod]
        public void QuienLoLleva_ElPapelNrDeSuNota_Cuenta()
        {
            var historia = new[] { Mov(1, "NR930553-1", TiposMovimientoBulto.RECOGIDO, HOY.AddHours(10), envio: null, pedido: 930553, tipoCodigo: CodigoBultoFurgoneta.NR) };

            Assert.IsNotNull(CandadoFurgonetaEnvio.QuienLoLleva(251104, 930553, historia));
            Assert.IsNull(CandadoFurgonetaEnvio.QuienLoLleva(251104, null, historia), "Sin pedido no es suyo");
        }

        [TestMethod]
        public void QuienLoLleva_BultosDeOtroEnvio_NoCuentan()
        {
            var historia = new[] { Mov(1, "RP251170-1", TiposMovimientoBulto.CARGADO, HOY.AddHours(9), envio: 251170) };

            Assert.IsNull(CandadoFurgonetaEnvio.QuienLoLleva(251169, null, historia));
        }

        [TestMethod]
        public void Mensaje_DeUstedConQuienYCuando()
        {
            var f = new BultosEnFurgoneta { Repartidor = "Andre", Desde = HOY.AddHours(9).AddMinutes(42) };

            Assert.AreEqual("No se puede: el envío 251169 lo lleva Andre en la furgoneta desde las 9:42. Que lo descargue o lo entregue primero.",
                CandadoFurgonetaEnvio.MensajeEnvio(251169, f, HOY.AddHours(9).AddMinutes(43)));
            StringAssert.Contains(CandadoFurgonetaEnvio.MensajeEnvio(251169, f, HOY.AddDays(1).AddHours(8)), "desde el " + HOY.ToString("dd/MM") + " a las 9:42");
        }

        [TestMethod]
        public void CambioQueFrena_AgenciaOEstadoTerminal()
        {
            var tramitado = new EnviosAgencia { Numero = 1, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Estado = Constantes.Agencias.ESTADO_TRAMITADO };

            Assert.IsTrue(CandadoFurgonetaEnvio.CambioQueFrena(tramitado, Constantes.Agencias.AGENCIA_CTT, Constantes.Agencias.ESTADO_TRAMITADO));
            Assert.IsTrue(CandadoFurgonetaEnvio.CambioQueFrena(tramitado, 14, Constantes.Agencias.ESTADO_ENTREGADO));
            Assert.IsTrue(CandadoFurgonetaEnvio.CambioQueFrena(tramitado, 14, Constantes.Agencias.ESTADO_DEVUELTO));
            Assert.IsTrue(CandadoFurgonetaEnvio.CambioQueFrena(tramitado, 14, Constantes.Agencias.ESTADO_CERRADO_A_MANO));
            Assert.IsFalse(CandadoFurgonetaEnvio.CambioQueFrena(tramitado, 14, Constantes.Agencias.ESTADO_INCIDENTADO));
            Assert.IsFalse(CandadoFurgonetaEnvio.CambioQueFrena(tramitado, 14, Constantes.Agencias.ESTADO_TRAMITADO), "Cambiar el reembolso, la fecha… sí se puede");
            var ctt = new EnviosAgencia { Numero = 2, Agencia = Constantes.Agencias.AGENCIA_CTT, Estado = Constantes.Agencias.ESTADO_TRAMITADO };
            Assert.IsFalse(CandadoFurgonetaEnvio.CambioQueFrena(ctt, 14, Constantes.Agencias.ESTADO_ENTREGADO), "Solo la 14");
        }

        // ================================================================ los endpoints

        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<EnvioHistoria> fakeHistoria;
        private List<EnvioHistoria> historiaGuardada;
        private ILectorFurgonetaEnvio lector;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeHistoria = A.Fake<DbSet<EnvioHistoria>>(o => o.Implements<IQueryable<EnvioHistoria>>().Implements<IDbAsyncEnumerable<EnvioHistoria>>());
            historiaGuardada = new List<EnvioHistoria>();
            A.CallTo(() => fakeHistoria.Add(A<EnvioHistoria>._)).ReturnsLazily((EnvioHistoria h) => { historiaGuardada.Add(h); return h; });
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistoria);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            lector = A.Fake<ILectorFurgonetaEnvio>();
            A.CallTo(() => lector.Leer(A<EnviosAgencia>._)).Returns(Task.FromResult<BultosEnFurgoneta>(null));
        }

        private class ControllerDePrueba : EnviosAgenciasController
        {
            public ControllerDePrueba(NVEntities db) : base(db) { }
            public List<string> Rastros { get; } = new List<string>();
            public List<string> Pasos { get; } = new List<string>();
            public EnviosAgencia ParaCandado { get; set; }
            internal override Task<int> BorrarHistoriaEnvio(int id) { Pasos.Add("historia"); return Task.FromResult(0); }
            internal override Task<string> LeerPapelEnReenvio(int id) => Task.FromResult<string>(null);
            internal override async Task EnTransaccionBorrado(Func<Task> trabajo) { Pasos.Add("begin"); await trabajo(); Pasos.Add("commit"); }
            internal override Task<List<EnvioHistoria>> LeerHistoriaParaRastro(int id)
                => Task.FromResult(new List<EnvioHistoria> { new EnvioHistoria { Numero = 9, NumeroEnvio = id, Campo = "Estado", ValorAnterior = "0", Usuario = "x" } });
            internal override Task ApuntarRastroBorrado(string json, string usuario) { Pasos.Add("rastro"); Rastros.Add(json); return Task.CompletedTask; }
            internal override Task<EnviosAgencia> LeerEnvioParaCandado(int id) => Task.FromResult(ParaCandado);
        }

        private ControllerDePrueba Controller(params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, @"NUEVAVISION\aida") };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, g)));
            return new ControllerDePrueba(db)
            {
                LectorFurgoneta = lector,
                MotivoPedidoRetenido = (e, p, rp) => Task.FromResult<string>(null),
                ResolverReenvioSobrante = null,
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            };
        }

        private EnviosAgencia ConEnvio(int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, short estado = Constantes.Agencias.ESTADO_TRAMITADO)
        {
            var envio = new EnviosAgencia
            {
                Numero = 251169, Empresa = "1  ", Pedido = 930001, Agencia = agencia, Estado = estado, CodigoBarras = "RP251169",
                Fecha = HOY, Nombre = "PELUQUERÍA ANA", Usuario = "almacen"
            };
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
            return envio;
        }

        private void EnFurgonetaDeAndre()
            => A.CallTo(() => lector.Leer(A<EnviosAgencia>._)).Returns(Task.FromResult(new BultosEnFurgoneta
            {
                Usuario = @"NUEVAVISION\Andre", Repartidor = "Andre", Desde = HOY.AddHours(9).AddMinutes(42), Codigos = new List<string> { "RP251169-1" }
            }));

        private static (HttpStatusCode, JObject) Leer(IHttpActionResult resultado)
        {
            var estado = (HttpStatusCode)resultado.GetType().GetProperty("StatusCode").GetValue(resultado);
            object contenido = resultado.GetType().GetProperty("Content").GetValue(resultado);
            return (estado, JObject.FromObject(contenido));
        }

        [TestMethod]
        public async Task DarPorCerrado_RepartoPropio_SiempreCierreNoPermitido()
        {
            EnviosAgencia envio = ConEnvio(estado: Constantes.Agencias.ESTADO_INCIDENTADO);

            (HttpStatusCode estado, JObject cuerpo) = Leer(await Controller().DarPorCerrado(envio.Numero, new DarPorCerradoEnvioDTO { Motivo = "Lo tiene el cliente" }));

            Assert.AreEqual(HttpStatusCode.Conflict, estado);
            Assert.AreEqual(CandadoFurgonetaEnvio.CODIGO_CIERRE_NO_PERMITIDO, (string)cuerpo["Codigo"]);
            Assert.AreEqual(CandadoFurgonetaEnvio.MENSAJE_CIERRE_NO_PERMITIDO, (string)cuerpo["Message"]);
            Assert.AreEqual(Constantes.Agencias.ESTADO_INCIDENTADO, envio.Estado, "No se toca");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        // Caso 251169: cargado a las 9:42, «dado por cerrado» a las 9:43.
        [TestMethod]
        public async Task DarPorCerrado_RepartoPropioEnFurgoneta_EnFurgonetaConQuienYDesde()
        {
            EnviosAgencia envio = ConEnvio();
            EnFurgonetaDeAndre();

            (HttpStatusCode estado, JObject cuerpo) = Leer(await Controller().DarPorCerrado(envio.Numero, new DarPorCerradoEnvioDTO { Motivo = "x" }));

            Assert.AreEqual(HttpStatusCode.Conflict, estado);
            Assert.AreEqual("EN_FURGONETA", (string)cuerpo["Codigo"]);
            Assert.AreEqual("Andre", (string)cuerpo["Repartidor"]);
            Assert.AreEqual(HOY.AddHours(9).AddMinutes(42), (DateTime)cuerpo["Desde"]);
            StringAssert.StartsWith((string)cuerpo["Message"], "No se puede: el envío 251169 lo lleva Andre en la furgoneta desde ");
            Assert.AreEqual("EN_FURGONETA", (string)cuerpo["error"]["code"]);
        }

        [TestMethod]
        public async Task DarPorCerrado_OtraAgencia_ComoSiempre()
        {
            EnviosAgencia envio = ConEnvio(agencia: Constantes.Agencias.AGENCIA_CTT, estado: Constantes.Agencias.ESTADO_INCIDENTADO);

            var resultado = await Controller().DarPorCerrado(envio.Numero, new DarPorCerradoEnvioDTO { Motivo = "Lo tiene el cliente" });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EnvioCerradoDTO>));
            A.CallTo(() => lector.Leer(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_RepartoPropioEnFurgoneta_409()
        {
            EnviosAgencia envio = ConEnvio();
            EnFurgonetaDeAndre();

            (HttpStatusCode estado, JObject cuerpo) = Leer(await Controller().AnularEnvio(envio.Numero));

            Assert.AreEqual(HttpStatusCode.Conflict, estado);
            Assert.AreEqual("EN_FURGONETA", (string)cuerpo["Codigo"]);
        }

        // Caso 251227: borrado con la etiqueta pegada.
        [TestMethod]
        public async Task Delete_RepartoPropioEnFurgoneta_409YNoBorraNada()
        {
            EnviosAgencia envio = ConEnvio();
            EnFurgonetaDeAndre();
            ControllerDePrueba sut = Controller();

            (HttpStatusCode estado, JObject cuerpo) = Leer(await sut.DeleteEnviosAgencia(envio.Numero, permitirEnCurso: true));

            Assert.AreEqual(HttpStatusCode.Conflict, estado);
            Assert.AreEqual("EN_FURGONETA", (string)cuerpo["Codigo"]);
            A.CallTo(() => fakeEnvios.Remove(A<EnviosAgencia>._)).MustNotHaveHappened();
            Assert.AreEqual(0, sut.Pasos.Count);
        }

        [TestMethod]
        public async Task Delete_CualquierAgencia_DejaRastroConElEnvioYSuHistoriaEnLaMismaTransaccion()
        {
            EnviosAgencia envio = ConEnvio(agencia: Constantes.Agencias.AGENCIA_CTT, estado: Constantes.Agencias.ESTADO_PENDIENTE);
            ControllerDePrueba sut = Controller();

            var resultado = await sut.DeleteEnviosAgencia(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EnviosAgencia>));
            CollectionAssert.AreEqual(new[] { "begin", "rastro", "historia", "commit" }, sut.Pasos);
            JObject rastro = JObject.Parse(sut.Rastros.Single());
            Assert.AreEqual(251169, (int)rastro["Envio"]["Numero"]);
            Assert.AreEqual("PELUQUERÍA ANA", (string)rastro["Envio"]["Nombre"]);
            Assert.AreEqual("Estado", (string)rastro["Historia"][0]["Campo"]);
            Assert.AreEqual(@"NUEVAVISION\aida", (string)rastro["BorradoPor"]);
            A.CallTo(() => fakeEnvios.Remove(envio)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void RastroBorrado_SoloColumnas_SinNavegaciones()
        {
            var envio = new EnviosAgencia { Numero = 1, Nombre = "ANA   ", AgenciasTransporte = new AgenciaTransporte() };

            Dictionary<string, object> columnas = RastroBorradoEnvio.Columnas(envio);

            Assert.AreEqual("ANA", columnas["Nombre"]);
            Assert.IsFalse(columnas.ContainsKey("AgenciasTransporte"));
            Assert.AreEqual("EnvioAgenciaBorrado", RastroBorradoEnvio.TABLA);
        }

        [TestMethod]
        public async Task ModificarDatos_PasarAEntregadoEnFurgoneta_409()
        {
            ControllerDePrueba sut = Controller();
            sut.ParaCandado = new EnviosAgencia { Numero = 251169, Agencia = 14, Estado = Constantes.Agencias.ESTADO_TRAMITADO };
            EnFurgonetaDeAndre();
            var servicio = A.Fake<ITramitacionEnviosService>();
            sut.TramitacionEnvios = servicio;

            (HttpStatusCode estado, JObject cuerpo) = Leer(await sut.ModificarDatosEnvio(251169, new ModificarDatosEnvioDTO { Estado = Constantes.Agencias.ESTADO_ENTREGADO }));

            Assert.AreEqual(HttpStatusCode.Conflict, estado);
            Assert.AreEqual("EN_FURGONETA", (string)cuerpo["Codigo"]);
            A.CallTo(() => servicio.ModificarDatosAsync(A<int>._, A<ModificarDatosEnvioDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ModificarDatos_CambiarReembolsoEnFurgoneta_Sigue()
        {
            ControllerDePrueba sut = Controller();
            sut.ParaCandado = new EnviosAgencia { Numero = 251169, Agencia = 14, Estado = Constantes.Agencias.ESTADO_TRAMITADO };
            EnFurgonetaDeAndre();
            var servicio = A.Fake<ITramitacionEnviosService>();
            sut.TramitacionEnvios = servicio;

            _ = await sut.ModificarDatosEnvio(251169, new ModificarDatosEnvioDTO { Estado = Constantes.Agencias.ESTADO_TRAMITADO, Reembolso = 10 });

            A.CallTo(() => servicio.ModificarDatosAsync(251169, A<ModificarDatosEnvioDTO>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Put_CambiarLaAgenciaEnFurgoneta_409()
        {
            var registrado = new EnviosAgencia { Numero = 251169, Empresa = "1  ", Agencia = 14, Estado = Constantes.Agencias.ESTADO_TRAMITADO };
            var lista = new List<EnviosAgencia> { registrado }.AsQueryable();
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).Provider).Returns(new TestDbAsyncQueryProvider<EnviosAgencia>(lista.Provider));
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).Expression).Returns(lista.Expression);
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).ElementType).Returns(lista.ElementType);
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).GetEnumerator()).ReturnsLazily(() => lista.GetEnumerator());
            A.CallTo(() => ((IDbAsyncEnumerable<EnviosAgencia>)fakeEnvios).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<EnviosAgencia>(lista.GetEnumerator()));
            A.CallTo(() => fakeEnvios.AsNoTracking()).Returns(fakeEnvios);
            EnFurgonetaDeAndre();
            var nuevo = new EnviosAgencia { Numero = 251169, Empresa = "1  ", Agencia = Constantes.Agencias.AGENCIA_CTT, Estado = Constantes.Agencias.ESTADO_TRAMITADO };

            (HttpStatusCode estado, JObject cuerpo) = Leer(await Controller().PutEnviosAgencia(251169, nuevo));

            Assert.AreEqual(HttpStatusCode.Conflict, estado);
            Assert.AreEqual("EN_FURGONETA", (string)cuerpo["Codigo"]);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        // ================================================================ POST {id}/Nota

        [TestMethod]
        public async Task Nota_ApuntaEnLaHistoriaSinTocarElEnvio()
        {
            EnviosAgencia envio = ConEnvio();

            var resultado = await Controller(Constantes.GruposSeguridad.ALMACEN).AnotarEnvio(envio.Numero, new EnviosAgenciasController.NotaEnvioDTO { Texto = "  La etiqueta está rota  " })
                as OkNegotiatedContentResult<EnviosAgenciasController.NotaEnvioAnotadaDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("La etiqueta está rota", resultado.Content.Texto);
            EnvioHistoria fila = historiaGuardada.Single();
            Assert.AreEqual("Nota", fila.Campo);
            Assert.AreEqual("La etiqueta está rota", fila.Observaciones);
            Assert.AreEqual(string.Empty, fila.ValorAnterior);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
        }

        [TestMethod]
        public async Task Nota_SinGrupo_403()
        {
            ConEnvio();

            (HttpStatusCode estado, JObject cuerpo) = Leer(await Controller().AnotarEnvio(251169, new EnviosAgenciasController.NotaEnvioDTO { Texto = "x" }));

            Assert.AreEqual(HttpStatusCode.Forbidden, estado);
            StringAssert.Contains((string)cuerpo["Message"], "Solo Almacén");
        }

        [TestMethod]
        public async Task Nota_SinTextoODemasiadoLarga_400_YNoExiste_404()
        {
            ConEnvio();
            ControllerDePrueba sut = Controller(Constantes.GruposSeguridad.INFORMATICA);

            Assert.IsInstanceOfType(await sut.AnotarEnvio(251169, new EnviosAgenciasController.NotaEnvioDTO { Texto = " " }), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await sut.AnotarEnvio(251169, new EnviosAgenciasController.NotaEnvioDTO { Texto = new string('x', 501) }), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeEnvios.FindAsync(5)).Returns(Task.FromResult<EnviosAgencia>(null));
            Assert.IsInstanceOfType(await sut.AnotarEnvio(5, new EnviosAgenciasController.NotaEnvioDTO { Texto = "x" }), typeof(NotFoundResult));
        }
    }
}
