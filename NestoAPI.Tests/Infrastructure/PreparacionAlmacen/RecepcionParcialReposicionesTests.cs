using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // NestoAPI#673 (Novedades 598, Reina): recibir una reposición por partes. Con Parcial entra SOLO lo leído (hasta lo enviado
    // de cada producto), el resto se queda en el diario con el mismo NºTraspaso y la reposición sigue pendiente; sin diferencias
    // ni faltas, sin cabecera recibida ni bultos leídos. La recepción que la termina compara solo lo que quedaba.
    [TestClass]
    public class RecepcionParcialReposicionesTests
    {
        private const int TRASPASO = 80862;

        private IRepositorioCierreReposiciones cierre;
        private ITransaccionCierreReposicion transaccion;
        private IAvisadorReposiciones avisador;
        private NestoAPI.Infraestructure.Reposiciones.IServicioDiferenciasReposicion diferencias;
        private List<string> pasos;

        [TestInitialize]
        public void Inicializar()
        {
            pasos = new List<string>();
            cierre = A.Fake<IRepositorioCierreReposiciones>();
            transaccion = A.Fake<ITransaccionCierreReposicion>();
            avisador = A.Fake<IAvisadorReposiciones>();
            diferencias = A.Fake<NestoAPI.Infraestructure.Reposiciones.IServicioDiferenciasReposicion>();
            A.CallTo(() => cierre.EnTransaccion(A<Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, A<bool>.Ignored))
                .ReturnsLazily((Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>> t, bool deshacer) => t(transaccion));
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(false);
            A.CallTo(() => transaccion.DiarioDeEntrada("1", "REI")).Returns("PendRepo");
            A.CallTo(() => transaccion.TraspasosEnDiario("1", "REI", "PendRepo")).Returns(new List<int> { TRASPASO, 80870 });
            A.CallTo(() => transaccion.LeerBultos("1", TRASPASO)).Returns(new List<FilaBultoReposicion>());
            A.CallTo(() => transaccion.LeerDatosTraspaso("1", "REI", "PendRepo", TRASPASO))
                .Returns(new DatosTraspasoReposicion { Origen = "ALG", Creador = "NUEVAVISION\\Andre" });
            Enviado(("A", 2), ("B", 3));

            A.CallTo(() => transaccion.ApartarOtros("1", "PendRepo", TRASPASO)).Invokes(() => pasos.Add("apartar otros")).Returns(new List<int> { 701 });
            A.CallTo(() => transaccion.ApartarNoLeido("1", "REI", "PendRepo", TRASPASO, A<IReadOnlyDictionary<string, int>>.Ignored))
                .Invokes((string e, string a, string d, int t, IReadOnlyDictionary<string, int> l) =>
                    pasos.Add("apartar no leído " + string.Join(",", l.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"))))
                .Returns(new List<int> { 900, 901 });
            A.CallTo(() => transaccion.AjustarALoLeido(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<int>.Ignored, A<string>.Ignored, A<int>.Ignored))
                .Invokes((string e, string a, string d, int t, string p, int c) => pasos.Add($"ajustar {p} a {c}"));
            A.CallTo(() => transaccion.LeerPendientesDeUbicar("1", "PendRepo")).Invokes(() => pasos.Add("leer pendientes de ubicar"));
            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", A<string>.Ignored)).Invokes(() => pasos.Add("contabilizar"));
            A.CallTo(() => transaccion.DevolverApartadas("1", "PendRepo", A<IReadOnlyCollection<int>>.Ignored))
                .Invokes((string e, string d, IReadOnlyCollection<int> a) => pasos.Add("devolver " + string.Join(",", a)));
            A.CallTo(() => transaccion.DejarPendientesDeUbicar("1", A<PendientesDeUbicarEntrada>.Ignored, A<string>.Ignored))
                .Invokes(() => pasos.Add("dejar pendientes de ubicar"));
            A.CallTo(() => transaccion.MarcarReposicionRecibida("1", TRASPASO, A<string>.Ignored)).Invokes(() => pasos.Add("cabecera recibida"));
            A.CallTo(() => transaccion.MarcarBultosLeidos("1", A<IEnumerable<string>>.Ignored, A<string>.Ignored)).Invokes(() => pasos.Add("bultos leídos"));
            A.CallTo(() => transaccion.RegistrarDiferencias(A<IEnumerable<NestoAPI.Infraestructure.Reposiciones.DiferenciaReposicionNueva>>.Ignored))
                .ReturnsLazily((IEnumerable<NestoAPI.Infraestructure.Reposiciones.DiferenciaReposicionNueva> d) =>
                {
                    pasos.Add("registrar diferencias " + string.Join(",", d.Select(x => $"{x.Producto}:{x.Enviado}/{x.Leido}")));
                    return new List<int> { 41 };
                });
        }

        private void Enviado(params (string producto, int cantidad)[] lineas)
        {
            A.CallTo(() => transaccion.LeerLineas("1", "REI", TRASPASO))
                .Returns(lineas.Select(l => new FilaReposicion { Producto = l.producto, Descripcion = "Producto " + l.producto, Cantidad = l.cantidad }).ToList());
        }

        private OrigenRecepcionReposiciones Reposiciones()
        {
            return new OrigenRecepcionReposiciones(A.Fake<IServicioRecepcionReposiciones>(), cierre, avisador, (e, u) => "REI",
                diferencias, A.Fake<IRepositorioBultosReposicion>());
        }

        private static IPrincipal Reina()
        {
            return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Reina") }, "prueba"));
        }

        private static SolicitudTerminarRecepcion Solicitud(bool parcial, params (string producto, int cantidad)[] lecturas)
        {
            return new SolicitudTerminarRecepcion
            {
                Empresa = "1", Almacen = "REI", Documento = TRASPASO.ToString(), IdRecepcion = Guid.NewGuid(),
                Lecturas = lecturas.ToDictionary(l => l.producto, l => l.cantidad),
                Usuario = "NUEVAVISION\\Reina", Principal = Reina(), Parcial = parcial
            };
        }

        [TestMethod]
        public async Task Parcial_DaDeAltaSoloLoLeidoYDevuelveAlDiarioLoDemasDelTraspasoYLasOtras()
        {
            ResultadoTerminarRecepcionDTO resultado = await Reposiciones().Terminar(Solicitud(true, ("A", 1)));

            CollectionAssert.AreEqual(new[]
            {
                "apartar otros", "apartar no leído A=1", "leer pendientes de ubicar", "contabilizar", "devolver 701,900,901", "dejar pendientes de ubicar"
            }, pasos, string.Join(" | ", pasos));
            Assert.IsTrue(resultado.Parcial);
            Assert.AreEqual(0, resultado.Diferencias.Count, "En una parcial no hay diferencias ni faltas");
            Assert.AreEqual(2, resultado.LineasPendientes, "Queda 1 de A y las 3 de B");
            Assert.AreEqual(4, resultado.UnidadesPendientes);
            StringAssert.Contains(string.Join(" ", resultado.Avisos), "Sigue pendiente de recibir el resto de la reposición (2 líneas, 4 ud.)");
            A.CallTo(() => transaccion.RegistrarDiferencias(A<IEnumerable<NestoAPI.Infraestructure.Reposiciones.DiferenciaReposicionNueva>>.Ignored))
                .MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(A<string>.Ignored, A<string>.Ignored, A<IEnumerable<string>>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => diferencias.AjustarOrigenTrasRecibir(A<string>.Ignored, A<IReadOnlyCollection<int>>.Ignored, A<string>.Ignored))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Parcial_DejaEvidenciaMarcadaComoParcialConQuienYDesdeDonde()
        {
            SolicitudTerminarRecepcion solicitud = Solicitud(true, ("A", 1), ("B", 3));
            solicitud.Dispositivo = "TIENDA-REI";

            _ = await Reposiciones().Terminar(solicitud);

            A.CallTo(() => transaccion.RegistrarEvidencia("1", A<IEnumerable<EvidenciaRecepcion>>.That.Matches(e =>
                e.Count() == 2 && e.All(f => f.Parcial && f.NumeroOrigen == TRASPASO && f.Usuario == "NUEVAVISION\\Reina" && f.Dispositivo == "TIENDA-REI")
                && e.Single(f => f.Producto == "A").Cantidad == 1)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Parcial_NoMarcaLaCabeceraNiLosBultosPeroExigeLaEtiqueta()
        {
            A.CallTo(() => transaccion.LeerBultos("1", TRASPASO)).Returns(new List<FilaBultoReposicion>
            {
                new FilaBultoReposicion { Traspaso = TRASPASO, Bulto = 1, Total = 1, Codigo = "R80862-1" }
            });
            SolicitudTerminarRecepcion sinEtiqueta = Solicitud(true, ("A", 1));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Reposiciones().Terminar(sinEtiqueta));
            StringAssert.Contains(ex.Message, "Lea la etiqueta del bulto");
            A.CallTo(() => transaccion.Contabilizar(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();

            SolicitudTerminarRecepcion conEtiqueta = Solicitud(true, ("A", 1));
            conEtiqueta.CodigosBulto = new List<string> { "R80862-1" };
            ResultadoTerminarRecepcionDTO resultado = await Reposiciones().Terminar(conEtiqueta);

            Assert.IsTrue(resultado.Parcial);
            A.CallTo(() => transaccion.MarcarReposicionRecibida(A<string>.Ignored, A<int>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => transaccion.MarcarBultosLeidos(A<string>.Ignored, A<IEnumerable<string>>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Parcial_ConAlgoQueNoVenia_SeRechazaSinTocarNada()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Reposiciones().Terminar(Solicitud(true, ("A", 1), ("Z", 1))));

            StringAssert.Contains(ex.Message, "Z no venía");
            StringAssert.Contains(ex.Message, "No se ha recibido nada");
            AssertNoSeHaTocadoElDiario();
        }

        [TestMethod]
        public async Task Parcial_ConMasDeLoEnviado_SeRechazaSinTocarNada()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Reposiciones().Terminar(Solicitud(true, ("A", 3))));

            StringAssert.Contains(ex.Message, "A (enviado 2, leído 3)");
            AssertNoSeHaTocadoElDiario();
        }

        private void AssertNoSeHaTocadoElDiario()
        {
            A.CallTo(() => transaccion.ApartarNoLeido(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<int>.Ignored, A<IReadOnlyDictionary<string, int>>.Ignored))
                .MustNotHaveHappened();
            A.CallTo(() => transaccion.AjustarALoLeido(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<int>.Ignored, A<string>.Ignored, A<int>.Ignored))
                .MustNotHaveHappened();
            A.CallTo(() => transaccion.Contabilizar(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => transaccion.RegistrarEvidencia(A<string>.Ignored, A<IEnumerable<EvidenciaRecepcion>>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Parcial_QueLoCompletaTodo_EsLaRecepcionEntera()
        {
            A.CallTo(() => transaccion.LeerBultos("1", TRASPASO)).Returns(new List<FilaBultoReposicion>
            {
                new FilaBultoReposicion { Traspaso = TRASPASO, Bulto = 1, Total = 1, Codigo = "R80862-1" }
            });
            SolicitudTerminarRecepcion solicitud = Solicitud(true, ("A", 2), ("B", 3));
            solicitud.CodigosBulto = new List<string> { "R80862-1" };

            ResultadoTerminarRecepcionDTO resultado = await Reposiciones().Terminar(solicitud);

            Assert.IsFalse(resultado.Parcial);
            CollectionAssert.AreEqual(new[]
            {
                "apartar otros", "leer pendientes de ubicar", "contabilizar", "devolver 701", "dejar pendientes de ubicar", "cabecera recibida", "bultos leídos"
            }, pasos, string.Join(" | ", pasos));
            A.CallTo(() => transaccion.RegistrarEvidencia("1", A<IEnumerable<EvidenciaRecepcion>>.That.Matches(e => e.All(f => !f.Parcial))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ParcialYDespuesLaFinal_LasDiferenciasSoloCuentanLoQueQuedaba()
        {
            OrigenRecepcionReposiciones reposiciones = Reposiciones();
            _ = await reposiciones.Terminar(Solicitud(true, ("A", 1)));
            // Tras la parcial, en el diario queda 1 de A y 3 de B (lo que lee el siguiente)
            Enviado(("A", 1), ("B", 3));
            pasos.Clear();

            ResultadoTerminarRecepcionDTO final = await reposiciones.Terminar(Solicitud(false, ("A", 1), ("B", 2)));

            Assert.IsFalse(final.Parcial);
            DiferenciaPreparacionDTO falta = final.Diferencias.Single();
            Assert.AreEqual("B", falta.Producto);
            Assert.AreEqual(3, falta.Esperado);
            Assert.AreEqual(2, falta.Leido);
            CollectionAssert.AreEqual(new[]
            {
                "apartar otros", "ajustar B a 2", "leer pendientes de ubicar", "contabilizar", "devolver 701", "dejar pendientes de ubicar",
                "cabecera recibida", "registrar diferencias B:3/2"
            }, pasos, string.Join(" | ", pasos));
            A.CallTo(() => avisador.Avisar("NUEVAVISION\\Andre", A<string>.Ignored, A<IEnumerable<string>>.Ignored)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DosParcialesSeguidas_CadaUnaDaDeAltaLoSuyoYLaReposicionSigueAbierta()
        {
            OrigenRecepcionReposiciones reposiciones = Reposiciones();
            ResultadoTerminarRecepcionDTO primera = await reposiciones.Terminar(Solicitud(true, ("A", 1)));
            Enviado(("A", 1), ("B", 3));
            ResultadoTerminarRecepcionDTO segunda = await reposiciones.Terminar(Solicitud(true, ("B", 2)));

            Assert.IsTrue(primera.Parcial);
            Assert.IsTrue(segunda.Parcial);
            Assert.AreEqual(2, segunda.LineasPendientes, "Queda 1 de A y 1 de B");
            Assert.AreEqual(2, segunda.UnidadesPendientes);
            CollectionAssert.AreEqual(new[] { "apartar no leído A=1", "apartar no leído B=2" },
                pasos.Where(p => p.StartsWith("apartar no leído")).ToArray());
            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", "NUEVAVISION\\Reina")).MustHaveHappenedTwiceExactly();
            A.CallTo(() => transaccion.MarcarReposicionRecibida(A<string>.Ignored, A<int>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Parcial_EnsayoFotografiaTrasApartarYSeDeshace()
        {
            var fotos = new List<string>();
            Func<Task<List<FilaEnsayoDTO>>> foto = () =>
            {
                fotos.Add("foto");
                return Task.FromResult(new List<FilaEnsayoDTO>());
            };
            A.CallTo(() => transaccion.PrepararFoto("1", "PendRepo", A<IReadOnlyCollection<int>>.Ignored)).Returns(foto);
            SolicitudTerminarRecepcion solicitud = Solicitud(true, ("A", 1));
            solicitud.Ensayo = new RegistroEnsayoRecepcion();

            ResultadoTerminarRecepcionDTO resultado = await Reposiciones().Terminar(solicitud);

            Assert.IsTrue(resultado.Parcial);
            Assert.IsNotNull(solicitud.Ensayo.TrasAjustar);
            Assert.AreEqual(3, fotos.Count, "Antes, tras apartar lo no leído y al acabar");
            A.CallTo(() => cierre.EnTransaccion(A<Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, true))
                .MustHaveHappenedOnceExactly();
        }

        // ------------------------------------------------------------------ Lo que queda en el diario (lo comprueba la SQL)

        [TestMethod]
        public void Descuadre_QuedaExactamenteLoLeido_Null()
        {
            Assert.IsNull(ApartadoTraspasosSql.Descuadre(
                new[] { new FilaReposicion { Producto = "a ", Cantidad = 1 }, new FilaReposicion { Producto = "B", Cantidad = 3 } },
                new Dictionary<string, int> { ["A"] = 1, ["B"] = 3 }));
        }

        [TestMethod]
        public void Descuadre_SiQuedaOtraCosa_DiceQue()
        {
            string descuadre = ApartadoTraspasosSql.Descuadre(
                new[] { new FilaReposicion { Producto = "A", Cantidad = 2 }, new FilaReposicion { Producto = "C", Cantidad = 1 } },
                new Dictionary<string, int> { ["A"] = 1, ["B"] = 3 });

            Assert.AreEqual("A: queda 2, leído 1; B: queda 0, leído 3; C: queda 1, leído 0", descuadre);
        }

        [TestMethod]
        public void SqlApartarNoLeido_ParteLaFilaQueCruzaYApartaElResto()
        {
            // La SQL no se puede ejecutar aquí: al menos que no pierda las piezas que la hacen segura
            string sql = ApartadoTraspasosSql.SQL_APARTAR_NO_LEIDO;
            StringAssert.Contains(sql, "WITH (UPDLOCK, HOLDLOCK)");
            StringAssert.Contains(sql, "ORDER BY [Nº Orden] ROWS UNBOUNDED PRECEDING");
            StringAssert.Contains(sql, "WHERE Antes < @p5 AND Antes + Cantidad > @p5");
            StringAssert.Contains(sql, "OUTPUT inserted.[Nº Orden] INTO @apartadas");
            StringAssert.Contains(sql, "f.Antes >= @p5");
            Assert.IsFalse(sql.Contains("ExtractoProducto"), "ExtractoProducto solo por prdExtrProducto");
        }

        // ------------------------------------------------------------------ La lista y el núcleo

        private static OrigenRecepcionReposiciones ReposicionesConPendientes(DateTime ahora, params ReposicionPendienteDTO[] pendientes)
        {
            var servicio = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => servicio.LeerPendientes("1", "REI")).Returns(pendientes.ToList());
            return new OrigenRecepcionReposiciones(servicio, A.Fake<IRepositorioCierreReposiciones>(), A.Fake<IAvisadorReposiciones>(), (e, u) => "REI",
                bultos: A.Fake<IRepositorioBultosReposicion>())
            { Ahora = () => ahora };
        }

        [TestMethod]
        public async Task Lista_LaRecibidaEnParteLoDiceYSiLlevaMasDeDosDiasAvisa()
        {
            var ahora = new DateTime(2026, 10, 19, 10, 0, 0);
            OrigenRecepcionReposiciones reposiciones = ReposicionesConPendientes(ahora,
                new ReposicionPendienteDTO { Traspaso = 80862, Lineas = 2, Unidades = 4, LineasRecibidas = 1, UnidadesRecibidas = 2, FechaPrimeraParcial = ahora.AddHours(-3) },
                new ReposicionPendienteDTO { Traspaso = 80863, Lineas = 1, Unidades = 1, LineasRecibidas = 2, UnidadesRecibidas = 5, FechaPrimeraParcial = ahora.AddDays(-2).AddMinutes(-1) },
                new ReposicionPendienteDTO { Traspaso = 80864, Lineas = 1, Unidades = 1 });
            var nucleo = new ServicioRecepciones(new IOrigenRecepcion[] { reposiciones });

            List<RecepcionPendienteDTO> lista = await nucleo.LeerPendientes("1", "REI");

            RecepcionPendienteDTO hoy = lista.Single(p => p.Documento == "80862");
            Assert.IsTrue(hoy.RecibidaEnParte);
            Assert.AreEqual("Recibido en parte: 1 línea, 2 ud.", hoy.TextoRecibidaEnParte);
            StringAssert.EndsWith(hoy.Detalle, "· Recibido en parte: 1 línea, 2 ud.");
            Assert.IsFalse(hoy.AvisarRecibidaEnParte);
            Assert.IsNull(hoy.AvisoRecibidaEnParte);

            RecepcionPendienteDTO antigua = lista.Single(p => p.Documento == "80863");
            Assert.AreEqual("Recibido en parte: 2 líneas, 5 ud.", antigua.TextoRecibidaEnParte);
            Assert.IsTrue(antigua.AvisarRecibidaEnParte);
            StringAssert.Contains(antigua.AvisoRecibidaEnParte, "más de 2 días recibida solo en parte");

            RecepcionPendienteDTO entera = lista.Single(p => p.Documento == "80864");
            Assert.IsFalse(entera.RecibidaEnParte);
            Assert.IsNull(entera.TextoRecibidaEnParte);
            Assert.IsFalse(entera.Detalle.Contains("Recibido en parte"));
        }

        [TestMethod]
        public async Task Nucleo_LaParcialLlegaALaEstrategiaYElMensajeDiceQueEsEnParte()
        {
            var origen = A.Fake<IOrigenRecepcion>(o => o.Implements<IAdmiteRecepcionParcial>());
            A.CallTo(() => origen.Tipo).Returns("REPO");
            A.CallTo(() => origen.SeTerminaDesdeAqui).Returns(true);
            A.CallTo(() => origen.PuedeTerminar(A<IPrincipal>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(true);
            SolicitudTerminarRecepcion recibida = null;
            A.CallTo(() => origen.Terminar(A<SolicitudTerminarRecepcion>.Ignored)).ReturnsLazily((SolicitudTerminarRecepcion s) =>
            {
                recibida = s;
                return new ResultadoTerminarRecepcionDTO { Tipo = "REPO", Documento = "80862", Parcial = true, Avisos = new List<string> { "Sigue pendiente…" } };
            });
            var nucleo = new ServicioRecepciones(new[] { origen });

            ResultadoTerminarRecepcionDTO resultado = await nucleo.Terminar("REPO", "1", "REI", "80862", new TerminarRecepcionDTO
            {
                IdRecepcion = Guid.NewGuid(),
                Parcial = true,
                Lecturas = new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = "A", Cantidad = 1 } }
            }, Reina());

            Assert.IsTrue(recibida.Parcial);
            StringAssert.StartsWith(resultado.Mensaje, "Recibido en parte: se ha dado de alta lo leído.");
            Assert.IsFalse(resultado.Mensaje.Contains("Recepción terminada"));
        }

        // Novedad 751 (reposición 81028, 23/10/26): se recibieron en parte 10 de 11 líneas y la que queda no ha llegado. Sin nada
        // que leer no se podía terminar nunca («No se ha recibido nada») y la reposición se quedaba en Entradas para siempre.
        [TestMethod]
        public async Task SinNadaLeidoTrasRecibirEnParte_LoQueQuedaEsDiferenciaYNoSeContabilizaNada()
        {
            Enviado(("B", 1));

            ResultadoTerminarRecepcionDTO final = await Reposiciones().Terminar(Solicitud(false));

            Assert.IsFalse(final.Parcial);
            DiferenciaPreparacionDTO falta = final.Diferencias.Single();
            Assert.AreEqual("B", falta.Producto);
            Assert.AreEqual(1, falta.Esperado);
            Assert.AreEqual(0, falta.Leido);
            CollectionAssert.AreEqual(new[]
            {
                "apartar otros", "ajustar B a 0", "devolver 701", "cabecera recibida", "registrar diferencias B:1/0"
            }, pasos, string.Join(" | ", pasos));
            A.CallTo(() => avisador.Avisar("NUEVAVISION\\Andre", A<string>.Ignored, A<IEnumerable<string>>.Ignored)).MustHaveHappenedOnceExactly();
        }

        private static IOrigenRecepcion OrigenReposicionPendiente(bool recibidaEnParte)
        {
            var origen = A.Fake<IOrigenRecepcion>(o => o.Implements<IAdmiteRecepcionParcial>());
            A.CallTo(() => origen.Tipo).Returns("REPO");
            A.CallTo(() => origen.SeTerminaDesdeAqui).Returns(true);
            A.CallTo(() => origen.PuedeTerminar(A<IPrincipal>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(true);
            A.CallTo(() => origen.LeerPendientes("1", "ALG")).Returns(new List<RecepcionPendienteDTO>
            {
                new RecepcionPendienteDTO { Tipo = "REPO", Documento = "81028", RecibidaEnParte = recibidaEnParte }
            });
            A.CallTo(() => origen.Terminar(A<SolicitudTerminarRecepcion>.Ignored))
                .Returns(new ResultadoTerminarRecepcionDTO { Tipo = "REPO", Documento = "81028" });
            return origen;
        }

        [TestMethod]
        public async Task Nucleo_SinNadaLeido_RecibidaEnParte_LaTermina()
        {
            IOrigenRecepcion origen = OrigenReposicionPendiente(recibidaEnParte: true);

            _ = await new ServicioRecepciones(new[] { origen }).Terminar("REPO", "1", "ALG", "81028",
                new TerminarRecepcionDTO { IdRecepcion = Guid.NewGuid() }, Reina());

            A.CallTo(() => origen.Terminar(A<SolicitudTerminarRecepcion>.That.Matches(s => !s.Lecturas.Any() && !s.Parcial)))
                .MustHaveHappenedOnceExactly();
        }

        [DataTestMethod]
        [DataRow(false, false, DisplayName = "Sin recibir en parte antes: sigue siendo «No se ha recibido nada»")]
        [DataRow(true, true, DisplayName = "Pedida como parcial: no daría de alta nada")]
        public async Task Nucleo_SinNadaLeido_EnLosDemasCasos_SeRechazaSinLlamarle(bool recibidaEnParte, bool parcial)
        {
            IOrigenRecepcion origen = OrigenReposicionPendiente(recibidaEnParte);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                new ServicioRecepciones(new[] { origen }).Terminar("REPO", "1", "ALG", "81028",
                    new TerminarRecepcionDTO { IdRecepcion = Guid.NewGuid(), Parcial = parcial }, Reina()));

            StringAssert.Contains(ex.Message, "No se ha recibido nada");
            A.CallTo(() => origen.Terminar(A<SolicitudTerminarRecepcion>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Nucleo_UnTipoQueNoAdmiteParcial_LaRechazaSinLlamarle()
        {
            var origen = A.Fake<IOrigenRecepcion>();
            A.CallTo(() => origen.Tipo).Returns("COMP");
            A.CallTo(() => origen.SeTerminaDesdeAqui).Returns(true);
            A.CallTo(() => origen.PuedeTerminar(A<IPrincipal>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(true);
            var nucleo = new ServicioRecepciones(new[] { origen });

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => nucleo.Terminar("COMP", "1", "ALG", "65", new TerminarRecepcionDTO
            {
                IdRecepcion = Guid.NewGuid(),
                Parcial = true,
                Lecturas = new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = "A", Cantidad = 1 } }
            }, Reina()));

            A.CallTo(() => origen.Terminar(A<SolicitudTerminarRecepcion>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Nucleo_AlAbrirla_DiceSiAdmiteRecibirEnParte()
        {
            var repo = A.Fake<IOrigenRecepcion>(o => o.Implements<IAdmiteRecepcionParcial>());
            A.CallTo(() => repo.Tipo).Returns("REPO");
            A.CallTo(() => repo.SeTerminaDesdeAqui).Returns(true);
            A.CallTo(() => repo.LeerEsperado("1", "REI", "80862")).Returns(new RecepcionDTO { Documento = "80862" });
            var comp = A.Fake<IOrigenRecepcion>();
            A.CallTo(() => comp.Tipo).Returns("COMP");
            A.CallTo(() => comp.SeTerminaDesdeAqui).Returns(true);
            A.CallTo(() => comp.LeerEsperado("1", "ALG", "65")).Returns(new RecepcionDTO { Documento = "65" });
            var nucleo = new ServicioRecepciones(new[] { repo, comp });

            Assert.IsTrue((await nucleo.LeerEsperado("REPO", "1", "REI", "80862", Reina())).AdmiteRecibirEnParte);
            Assert.IsFalse((await nucleo.LeerEsperado("COMP", "1", "ALG", "65", Reina())).AdmiteRecibirEnParte);
        }

        [TestMethod]
        public async Task Reposiciones_AlAbrirla_DiceLoYaRecibidoEnParte()
        {
            var servicio = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => servicio.LeerPendientes("1", "REI")).Returns(new List<ReposicionPendienteDTO>
            {
                new ReposicionPendienteDTO { Traspaso = 80862, Lineas = 1, Unidades = 1, LineasRecibidas = 1, UnidadesRecibidas = 2, FechaPrimeraParcial = DateTime.Now }
            });
            A.CallTo(() => servicio.LeerRecepcion("1", "REI", 80862)).Returns(new RecepcionReposicionDTO
            {
                Empresa = "1", Almacen = "REI", Traspaso = 80862,
                Lineas = new List<LineaReposicionDTO> { new LineaReposicionDTO { Producto = "B", Cantidad = 1 } }
            });
            var reposiciones = new OrigenRecepcionReposiciones(servicio, A.Fake<IRepositorioCierreReposiciones>(), A.Fake<IAvisadorReposiciones>(), (e, u) => "REI",
                bultos: A.Fake<IRepositorioBultosReposicion>());

            RecepcionDTO recepcion = await reposiciones.LeerEsperado("1", "REI", "80862");

            Assert.AreEqual("Recibido en parte: 1 línea, 2 ud.", recepcion.TextoRecibidaEnParte);
        }
    }
}
