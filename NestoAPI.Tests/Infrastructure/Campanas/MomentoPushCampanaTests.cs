using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Campanas
{
    /// <summary>El «momento» de la push de campaña (27/11/26): Arranque, Recordatorio y Cierre. Nada de una campaña en concreto.</summary>
    [TestClass]
    public class MomentoPushCampanaTests
    {
        private const string CAMPANA = "Rebajas de Primavera 2027";
        private const string TITULO = "Rebajas de Primavera";
        // La campaña de las pruebas acaba el lunes 30/11/26 (fila sin hora: hasta las 23:59)
        private static readonly DateTime FIN = new DateTime(2026, 11, 30);
        private static readonly DateTime AHORA = new DateTime(2026, 11, 27, 10, 30, 0);

        // ================================================================ la regla del momento

        [TestMethod]
        public void Momento_PorDefectoArranque_SinMayusculas_YDesconocido400()
        {
            Assert.AreEqual(MomentosPushCampana.ARRANQUE, MomentosPushCampana.Normalizar(null));
            Assert.AreEqual(MomentosPushCampana.ARRANQUE, MomentosPushCampana.Normalizar(" "));
            Assert.AreEqual(MomentosPushCampana.RECORDATORIO, MomentosPushCampana.Normalizar("recordatorio"));
            Assert.AreEqual(MomentosPushCampana.CIERRE, MomentosPushCampana.Normalizar(" CIERRE "));
            Assert.IsNull(MomentosPushCampana.Normalizar("Final"));

            Assert.IsNull(ReglaPushCampana.Validar(new PeticionCampanaPushDTO { Campana = CAMPANA }, out PeticionPushCampanaValida v));
            Assert.AreEqual(MomentosPushCampana.ARRANQUE, v.Momento);
            Assert.AreEqual(ReglaMomentoPushCampana.MENSAJE_MOMENTO_DESCONOCIDO,
                ReglaPushCampana.Validar(new PeticionCampanaPushDTO { Campana = CAMPANA, Momento = "Final" }, out _));
        }

        [TestMethod]
        public void Fin_SinHoraEsElDiaEntero_ConHoraLaSuya_YElMasTardio()
        {
            var sinHora = new CandidataOferta { Campana = CAMPANA, FechaHasta = FIN, FechaHastaExacta = FIN };
            Assert.AreEqual(new DateTime(2026, 11, 30, 23, 59, 0), ReglaMomentoPushCampana.Fin(new[] { sinHora }, CAMPANA));

            var conHora = new CandidataOferta { Campana = CAMPANA, FechaHasta = FIN, FechaHastaExacta = FIN.AddHours(14) };
            Assert.AreEqual(FIN.AddHours(14), ReglaMomentoPushCampana.Fin(new[] { conHora }, CAMPANA));
            Assert.AreEqual(new DateTime(2026, 11, 30, 23, 59, 0), ReglaMomentoPushCampana.Fin(new[] { conHora, sinHora }, CAMPANA),
                "Una fila sin hora acaba al final del día, más tarde que la de las 14:00");

            Assert.IsNull(ReglaMomentoPushCampana.Fin(new[] { sinHora, new CandidataOferta { Campana = CAMPANA } }, CAMPANA), "Una fila sin fin: la campaña no tiene fin");
            Assert.IsNull(ReglaMomentoPushCampana.Fin(new[] { sinHora }, "Otra"));
        }

        [TestMethod]
        public void ValidarCierre_SoloSiAcabaHoyOManana()
        {
            DateTime fin = new DateTime(2026, 11, 30, 23, 59, 0);
            Assert.IsNull(ReglaMomentoPushCampana.ValidarCierre(fin, new DateTime(2026, 11, 30, 10, 0, 0)), "Acaba hoy");
            Assert.IsNull(ReglaMomentoPushCampana.ValidarCierre(fin, new DateTime(2026, 11, 29, 20, 0, 0)), "Acaba mañana");
            StringAssert.Contains(ReglaMomentoPushCampana.ValidarCierre(fin, new DateTime(2026, 11, 28, 10, 0, 0)), "acaba hoy o mañana");
            StringAssert.Contains(ReglaMomentoPushCampana.ValidarCierre(fin, new DateTime(2026, 11, 28, 10, 0, 0)), "Use «Recordatorio»");
            StringAssert.Contains(ReglaMomentoPushCampana.ValidarCierre(null, AHORA), "no tiene fecha de fin");
            StringAssert.Contains(ReglaMomentoPushCampana.ValidarCierre(new DateTime(2026, 11, 30, 14, 0, 0), new DateTime(2026, 11, 30, 15, 0, 0)), "ya ha terminado");
        }

        private static OfertaElegida O(string producto, string motivo, bool personal = true)
            => new OfertaElegida
            {
                Candidata = new CandidataOferta { Producto = producto, Nombre = "PRODUCTO " + producto, Campana = CAMPANA, Descuento = 0.30m, Familia = "F" + producto },
                Motivo = motivo,
                Texto = "texto de " + producto,
                Personal = personal
            };

        private static List<OfertaElegida> Ranking() => new List<OfertaElegida>
        {
            O("1", MotivosOfertaParaTi.COMPRADO_JUNTO),
            O("2", MotivosOfertaParaTi.TE_PUEDE_INTERESAR),
            O("3", MotivosOfertaParaTi.LO_SUELES_PEDIR),
            O("4", MotivosOfertaParaTi.PARECIDO),
            O("5", MotivosOfertaParaTi.TE_TOCA),
            O("6", MotivosOfertaParaTi.DE_TUS_MARCAS),
            O("7", MotivosOfertaParaTi.TE_PUEDE_INTERESAR, personal: false)
        };

        private static Dictionary<string, InteresOfertaCliente> Intereses(params InteresOfertaCliente[] i)
            => i.ToDictionary(x => x.Producto, x => x, StringComparer.OrdinalIgnoreCase);

        [TestMethod]
        public void Lista_Arranque_LoPersonalDelRankingComoSiempre()
        {
            ListaPushCliente l = ReglaMomentoPushCampana.Lista(MomentosPushCampana.ARRANQUE, Ranking(),
                Intereses(new InteresOfertaCliente { Producto = "1", AnunciadaPorPush = true }));
            CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5", "6" }, l.Lista.Select(o => o.Candidata.Producto).ToList());
            Assert.AreEqual(0, l.QuitadasYaAnunciadas, "En el arranque no se quita nada");
        }

        [TestMethod]
        public void Lista_Recordatorio_SinLoAnunciado_YPrimeroLoQueConvierteEnSuOrden()
        {
            ListaPushCliente l = ReglaMomentoPushCampana.Lista(MomentosPushCampana.RECORDATORIO, Ranking(), Intereses(
                new InteresOfertaCliente { Producto = "3", AnunciadaPorPush = true },
                new InteresOfertaCliente { Producto = "1", AnunciadaPorPush = true },
                new InteresOfertaCliente { Producto = "4", VistaEl = AHORA }));

            // 3 y 1 ya anunciados; delante 5 (TeToca) y 6 (DeTusMarcas) en el orden del ranking; luego 2 y 4
            CollectionAssert.AreEqual(new[] { "5", "6", "2", "4" }, l.Lista.Select(o => o.Candidata.Producto).ToList());
            Assert.AreEqual(2, l.QuitadasYaAnunciadas);
            Assert.AreEqual(MotivosOfertaParaTi.PARECIDO, l.Lista.Last().Motivo, "En el recordatorio lo visto no cambia de motivo");
        }

        [TestMethod]
        public void Lista_Cierre_PrimeroAnadidasAbiertasYVistas_AunqueYaSeAnunciasen_YElRestoSinLoAnunciado()
        {
            List<OfertaElegida> ranking = Ranking();
            ListaPushCliente l = ReglaMomentoPushCampana.Lista(MomentosPushCampana.CIERRE, ranking, Intereses(
                new InteresOfertaCliente { Producto = "6", VistaEl = AHORA },
                new InteresOfertaCliente { Producto = "4", VistaEl = AHORA, AbiertaEl = AHORA, AnunciadaPorPush = true },
                new InteresOfertaCliente { Producto = "7", VistaEl = AHORA, AbiertaEl = AHORA, AnadidaEl = AHORA },
                new InteresOfertaCliente { Producto = "2", VistaEl = AHORA },
                new InteresOfertaCliente { Producto = "1", AnunciadaPorPush = true }));

            // añadida 7 (aunque no sea «personal»: la vio en «Ver todas»), abierta 4 (ya anunciada, se repite), vistas 2 y 6 por ranking;
            // luego lo personal sin lo anunciado (1 fuera): 3, 5
            CollectionAssert.AreEqual(new[] { "7", "4", "2", "6", "3", "5" }, l.Lista.Select(o => o.Candidata.Producto).ToList());
            Assert.AreEqual(1, l.QuitadasYaAnunciadas);
            Assert.IsTrue(l.Lista.Take(4).All(o => o.Motivo == MotivosOfertaParaTi.LO_MIRASTE && o.Personal));
            Assert.AreEqual(ReglaMomentoPushCampana.TEXTO_ANADIDA, l.Lista[0].Texto);
            Assert.AreEqual(ReglaMomentoPushCampana.TEXTO_ABIERTA, l.Lista[1].Texto);
            Assert.AreEqual("Sigue con descuento", l.Lista[2].Texto, "De lo solo visto no se dice que lo miró con interés");
            Assert.AreEqual(MotivosOfertaParaTi.LO_SUELES_PEDIR, l.Lista[4].Motivo);
            // el ranking guardado (lo comparten la franja y «Ver todas») no se toca
            Assert.AreEqual(MotivosOfertaParaTi.PARECIDO, ranking[3].Motivo);
            Assert.IsFalse(ranking[6].Personal);
        }

        [TestMethod]
        public void Lista_SinIntereses_RecordatorioYCierreSinQuitarNada()
        {
            ListaPushCliente l = ReglaMomentoPushCampana.Lista(MomentosPushCampana.CIERRE, Ranking(), null);
            Assert.AreEqual(6, l.Lista.Count);
            Assert.AreEqual(0, l.QuitadasYaAnunciadas);
        }

        // ================================================================ textos

        private static List<OfertaElegida> Nombrables(string nombre, string motivo = MotivosOfertaParaTi.LO_SUELES_PEDIR)
            => new List<OfertaElegida>
            {
                new OfertaElegida { Candidata = new CandidataOferta { Producto = "45929", Nombre = nombre, Descuento = 0.30m }, Motivo = motivo }
            };

        [TestMethod]
        public void Textos_Arranque_LosDeSiempreConElTituloDeLaCampana()
        {
            List<OfertaElegida> n = Nombrables("SICUREZZA BB CREAM");
            Assert.AreEqual(TITULO, TextosMomentoPushCampana.Titulo(MomentosPushCampana.ARRANQUE, TITULO, FIN, AHORA));
            Assert.AreEqual("Sicurezza BB Cream (−30 %) y 2 más pensados para ti.", TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.ARRANQUE, TITULO, FIN, AHORA, n, 3));
            Assert.AreEqual(TextosPushOfertas.CuerpoAutomatico(n, 3), TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.ARRANQUE, TITULO, FIN, AHORA, n, 3));
            Assert.AreEqual("Mira las ofertas del Rebajas de Primavera que hemos elegido para ti.",
                TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.ARRANQUE, TITULO, FIN, AHORA, new List<OfertaElegida>(), 3));
        }

        [TestMethod]
        public void Textos_Recordatorio_SingularPluralYGenerico()
        {
            Assert.AreEqual("Rebajas de Primavera: más ofertas para ti", TextosMomentoPushCampana.Titulo(MomentosPushCampana.RECORDATORIO, TITULO, FIN, AHORA));
            List<OfertaElegida> n = Nombrables("SICUREZZA BB CREAM");
            Assert.AreEqual("Te hemos elegido otras: Sicurezza BB Cream (−30 %) y 2 más.", TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.RECORDATORIO, TITULO, FIN, AHORA, n, 3));
            Assert.AreEqual("Te hemos elegido otras: Sicurezza BB Cream (−30 %) y 1 más.", TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.RECORDATORIO, TITULO, FIN, AHORA, n, 2));
            string uno = TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.RECORDATORIO, TITULO, FIN, AHORA, n, 1);
            Assert.AreEqual("Te hemos elegido otra: Sicurezza BB Cream (−30 %).", uno);
            Assert.IsFalse(uno.Contains("y 0 más"));
            Assert.AreEqual("Te hemos elegido otras ofertas de Rebajas de Primavera. ¡Échales un vistazo!",
                TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.RECORDATORIO, TITULO, FIN, AHORA, new List<OfertaElegida>(), 3));
        }

        [TestMethod]
        public void Textos_Cierre_HoyYManana_ConLaHoraDeLaCampana()
        {
            DateTime fin = new DateTime(2026, 11, 30, 23, 59, 0);
            DateTime hoy = new DateTime(2026, 11, 30, 10, 0, 0);
            DateTime vispera = new DateTime(2026, 11, 29, 10, 0, 0);
            List<OfertaElegida> n = Nombrables("SICUREZZA BB CREAM");

            Assert.AreEqual("Último día de Rebajas de Primavera", TextosMomentoPushCampana.Titulo(MomentosPushCampana.CIERRE, TITULO, fin, hoy));
            Assert.AreEqual("Termina hoy a las 23:59: Sicurezza BB Cream (−30 %) y 2 más.", TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.CIERRE, TITULO, fin, hoy, n, 3));
            Assert.AreEqual("Termina hoy a las 23:59: Sicurezza BB Cream (−30 %).", TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.CIERRE, TITULO, fin, hoy, n, 1));

            Assert.AreEqual("Rebajas de Primavera: termina mañana", TextosMomentoPushCampana.Titulo(MomentosPushCampana.CIERRE, TITULO, fin, vispera));
            Assert.AreEqual("Termina mañana: Sicurezza BB Cream (−30 %) y 1 más.", TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.CIERRE, TITULO, fin, vispera, n, 2));
            Assert.AreEqual("Termina mañana a las 14:00: Sicurezza BB Cream (−30 %).",
                TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.CIERRE, TITULO, new DateTime(2026, 11, 30, 14, 0, 0), vispera, n, 1));
            Assert.AreEqual("Termina hoy a las 23:59: mira las ofertas que hemos elegido para ti.",
                TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.CIERRE, TITULO, fin, hoy, new List<OfertaElegida>(), 2));
        }

        [TestMethod]
        public void Textos_Cierre_LoQueMiro_SigueAlDescuentoHastaEstaNocheOManana()
        {
            DateTime fin = new DateTime(2026, 11, 30, 23, 59, 0);
            List<OfertaElegida> n = Nombrables("SICUREZZA BB CREAM", MotivosOfertaParaTi.LO_MIRASTE);
            Assert.AreEqual("Sicurezza BB Cream sigue al −30 % hasta esta noche.",
                TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.CIERRE, TITULO, fin, new DateTime(2026, 11, 30, 10, 0, 0), n, 3));
            Assert.AreEqual("Sicurezza BB Cream sigue al −30 % hasta mañana.",
                TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.CIERRE, TITULO, fin, new DateTime(2026, 11, 29, 10, 0, 0), n, 3));
            Assert.AreEqual("Sicurezza BB Cream sigue al −30 % hasta las 14:00.",
                TextosMomentoPushCampana.Cuerpo(MomentosPushCampana.CIERRE, TITULO, new DateTime(2026, 11, 30, 14, 0, 0), new DateTime(2026, 11, 30, 10, 0, 0), n, 3));
        }

        [TestMethod]
        public void Textos_Recortes_ConPuntosYDentroDeLosLimites()
        {
            const string LARGO = "CREMA HIDRATANTE INTENSIVA CON ÁCIDO HIALURÓNICO Y VITAMINA C PARA PIELES MUY SECAS 50 ML";
            const string CAMPANA_LARGA = "Gran Liquidación de Fin de Temporada de Otoño e Invierno";
            DateTime fin = new DateTime(2026, 11, 30, 23, 59, 0);
            DateTime hoy = new DateTime(2026, 11, 30, 10, 0, 0);
            foreach (string momento in new[] { MomentosPushCampana.RECORDATORIO, MomentosPushCampana.CIERRE })
            {
                string titulo = TextosMomentoPushCampana.Titulo(momento, CAMPANA_LARGA, fin, hoy);
                Assert.IsTrue(titulo.Length <= TextosMomentoPushCampana.MAXIMO_TITULO, titulo);
                StringAssert.Contains(titulo, "…");
                foreach (string motivo in new[] { MotivosOfertaParaTi.LO_SUELES_PEDIR, MotivosOfertaParaTi.LO_MIRASTE })
                {
                    string cuerpo = TextosMomentoPushCampana.Cuerpo(momento, CAMPANA_LARGA, fin, hoy, Nombrables(LARGO, motivo), 9);
                    Assert.IsTrue(cuerpo.Length <= TextosMomentoPushCampana.MAXIMO_CUERPO, cuerpo);
                    StringAssert.Contains(cuerpo, "…");
                    StringAssert.StartsWith(cuerpo.Replace("Termina hoy a las 23:59: ", "").Replace("Te hemos elegido otras: ", ""), "Crema Hidratante");
                }
                string generico = TextosMomentoPushCampana.Cuerpo(momento, CAMPANA_LARGA, fin, hoy, new List<OfertaElegida>(), 9);
                Assert.IsTrue(generico.Length <= TextosMomentoPushCampana.MAXIMO_CUERPO, generico);
            }
            Assert.AreEqual("Crema Hidratante…", TextosMomentoPushCampana.Recortar("Crema Hidratante Intensiva", 20));
            Assert.AreEqual("Corto", TextosMomentoPushCampana.Recortar("Corto", 20));
        }

        [TestMethod]
        public void Textos_NingunoLlevaElNombreDeUnaCampanaAFuego()
        {
            DateTime fin = new DateTime(2026, 11, 30, 23, 59, 0);
            foreach (string momento in new[] { MomentosPushCampana.ARRANQUE, MomentosPushCampana.RECORDATORIO, MomentosPushCampana.CIERRE })
            {
                string todo = TextosMomentoPushCampana.Titulo(momento, "Feria", fin, fin.Date) + " "
                    + TextosMomentoPushCampana.Cuerpo(momento, "Feria", fin, fin.Date, new List<OfertaElegida>(), 2) + " "
                    + TextosMomentoPushCampana.Cuerpo(momento, "Feria", fin, fin.Date, Nombrables("X"), 2);
                Assert.IsFalse(todo.IndexOf("Black", StringComparison.OrdinalIgnoreCase) >= 0, todo);
            }
        }

        [TestMethod]
        public void Notificacion_ConMomento_SinProductos()
        {
            NotificacionPushDTO n = TextosPushOfertas.Notificacion(CAMPANA, "t", "c", null, MomentosPushCampana.CIERRE);
            Assert.AreEqual("Cierre", n.Datos["momento"]);
            Assert.AreEqual("ofertas", n.Datos["tipo"]);
            Assert.AreEqual(CAMPANA, n.Datos["campana"]);
            Assert.IsFalse(n.Datos.ContainsKey("productos"));
        }

        // ================================================================ el servicio de la push

        private ILectorOfertasParaTi lector;
        private IPuntuadorReposicion puntuador;
        private IFuentesSugerenciasOfertas fuentes;
        private ICalculadorPreciosOferta precios;
        private List<Exception> errores;

        private static FilaCampanaOferta Fila(string producto) => new FilaCampanaOferta
        {
            Campana = CAMPANA,
            Producto = producto,
            Descuento = 0.30m,
            AudienciaOferta = 2,
            FechaDesde = new DateTime(2026, 11, 25),
            FechaHasta = FIN,
            Nombre = "PRODUCTO " + producto,
            Familia = "F" + producto,
            Grupo = "COS",
            SubGrupo = "CRE",
            PVP = 10,
            Estado = 0
        };

        [TestInitialize]
        public void Preparar()
        {
            errores = new List<Exception>();
            lector = A.Fake<ILectorOfertasParaTi>();
            puntuador = A.Fake<IPuntuadorReposicion>();
            fuentes = A.Fake<IFuentesSugerenciasOfertas>();
            precios = A.Fake<ICalculadorPreciosOferta>();
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._))
                .Returns(Enumerable.Range(1, 6).Select(i => Fila(i.ToString(CultureInfo.InvariantCulture))).ToList());
            A.CallTo(() => lector.LeerPopularidad(A<IReadOnlyCollection<string>>._)).Returns(new Dictionary<string, int>());
            A.CallTo(() => lector.LeerDisponibles(A<IReadOnlyCollection<string>>._))
                .ReturnsLazily((IReadOnlyCollection<string> ps) => ps.ToDictionary(p => p, p => 5));
            A.CallTo(() => lector.LeerCompras(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._, A<DateTime>._)).Returns(new List<CompraDiaReposicion>());
            A.CallTo(() => lector.LeerPendientes(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(new List<PendienteClienteOferta>());
            A.CallTo(() => lector.LeerFamilias(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(new Dictionary<string, string>());
            // El modelo puntúa todo: «1» el que más
            A.CallTo(() => puntuador.ModeloDisponible()).Returns("m1-x");
            A.CallTo(() => puntuador.Puntuar(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._))
                .ReturnsLazily((string e, string c, DateTime d, IReadOnlyCollection<string> ps)
                    => ps.ToDictionary(p => p, p => 1f - int.Parse(p, CultureInfo.InvariantCulture) / 100f));
            // «3» y «4» le vienen por comprado junto (se pueden nombrar)
            A.CallTo(() => fuentes.Leer(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<CompraDiaReposicion>>._, A<ISet<string>>._))
                .Returns(new FuentesClienteOfertas
                {
                    CompradoJunto = new Dictionary<string, Tuple<decimal, string>>
                    {
                        { "3", Tuple.Create(0.5m, "JABON ISDRUC") },
                        { "4", Tuple.Create(0.4m, "JABON ISDRUC") }
                    }
                });
            A.CallTo(() => precios.Calcular(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .Returns(new PreciosOferta { Precio = 7m, PrecioAntes = 10m, Tarifa = 10m });
        }

        private IFuentePushCampana Fuente(InteresesPushCampana intereses = null)
        {
            IFuentePushCampana fuente = A.Fake<IFuentePushCampana>();
            A.CallTo(() => fuente.ClientesConConsejos()).Returns(new List<string> { "1000" });
            A.CallTo(() => fuente.ClientesDeBaja()).Returns(new HashSet<string>());
            A.CallTo(() => fuente.ClientesConPushDeCampana(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._)).Returns(new HashSet<string>());
            A.CallTo(() => fuente.LeerIntereses(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(intereses ?? new InteresesPushCampana());
            return fuente;
        }

        private static InteresesPushCampana Registro(params InteresOfertaCliente[] delCliente1000)
        {
            var r = new InteresesPushCampana { TablaExiste = true };
            r.PorCliente["1000"] = delCliente1000.ToDictionary(x => x.Producto, x => x, StringComparer.OrdinalIgnoreCase);
            return r;
        }

        private ServicioPushCampana Servicio(IFuentePushCampana fuente, IServicioNotificacionesPush push = null, IRegistroOfertasMostradas registro = null)
            => new ServicioPushCampana(new ServicioOfertasParaTi(lector, puntuador, errores.Add, fuentes), fuente, push ?? A.Fake<IServicioNotificacionesPush>(),
                precios, c => Task.FromResult("0"), p => Task.FromResult<string>(null), errores.Add, registro: registro);

        [TestMethod]
        public async Task Servicio_ArranquePorDefecto_ComoSiempre_YSinLeerElRegistro()
        {
            IFuentePushCampana fuente = Fuente();
            ResultadoPushCampana r = await Servicio(fuente).Ejecutar(new PeticionCampanaPushDTO { Campana = CAMPANA, Maximo = 3 }, true, AHORA, "Carlos");

            MensajePushCampanaDTO m = r.Respuesta.Mensajes.Single();
            Assert.AreEqual(MomentosPushCampana.ARRANQUE, r.Respuesta.Momento);
            Assert.AreEqual(MomentosPushCampana.ARRANQUE, m.Momento);
            Assert.AreEqual(TITULO, m.Titulo);
            Assert.AreEqual("Producto 3 (−30 %) y 2 más pensados para ti.", m.Cuerpo, "El texto de siempre: solo se nombra lo suyo");
            Assert.AreEqual("2026-11-30 23:59", r.Respuesta.Fin);
            Assert.IsNull(r.Respuesta.Aviso);
            Assert.AreEqual(0, m.QuitadasYaAnunciadas);
            CollectionAssert.AreEqual(m.Productos, m.Ofertas.Select(o => o.Producto).ToList());
            Assert.AreEqual(MotivosOfertaParaTi.COMPRADO_JUNTO, m.Ofertas.Single(o => o.Producto == "3").Motivo);
            A.CallTo(() => fuente.LeerIntereses(A<string>._, A<IReadOnlyCollection<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_Recordatorio_QuitaLoAnunciado_UnaLecturaParaTodos_YSusTextos()
        {
            IFuentePushCampana fuente = Fuente(Registro(
                new InteresOfertaCliente { Producto = "1", AnunciadaPorPush = true },
                new InteresOfertaCliente { Producto = "3", AnunciadaPorPush = true }));
            ResultadoPushCampana r = await Servicio(fuente).Ejecutar(new PeticionCampanaPushDTO { Campana = CAMPANA, Maximo = 3, Momento = "Recordatorio" }, true, AHORA, "Carlos");

            MensajePushCampanaDTO m = r.Respuesta.Mensajes.Single();
            Assert.AreEqual("Recordatorio", m.Momento);
            Assert.AreEqual(2, m.QuitadasYaAnunciadas);
            Assert.IsFalse(m.Productos.Contains("1") || m.Productos.Contains("3"));
            Assert.AreEqual("Rebajas de Primavera: más ofertas para ti", m.Titulo);
            Assert.AreEqual("Te hemos elegido otras: Producto 4 (−30 %) y 2 más.", m.Cuerpo);
            A.CallTo(() => fuente.LeerIntereses(CAMPANA, A<IReadOnlyCollection<string>>.That.Matches(c => c.Count == 1 && c.Contains("1000"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Servicio_Recordatorio_TodoAnunciado_SeSaltaConSuMotivo()
        {
            IFuentePushCampana fuente = Fuente(Registro(Enumerable.Range(1, 6)
                .Select(i => new InteresOfertaCliente { Producto = i.ToString(CultureInfo.InvariantCulture), AnunciadaPorPush = true }).ToArray()));
            ResultadoPushCampana r = await Servicio(fuente).Ejecutar(new PeticionCampanaPushDTO { Campana = CAMPANA, Momento = "Recordatorio" }, true, AHORA, "Carlos");

            Assert.AreEqual(0, r.Respuesta.Mensajes.Count);
            Assert.AreEqual(ReglaMomentoPushCampana.MOTIVO_YA_ANUNCIADAS, r.Respuesta.Saltados.Single().Motivo);
        }

        [TestMethod]
        public async Task Servicio_SinLaTabla_NoQuitaNada_YElEnsayoLoDice()
        {
            ResultadoPushCampana r = await Servicio(Fuente(new InteresesPushCampana { TablaExiste = false }))
                .Ejecutar(new PeticionCampanaPushDTO { Campana = CAMPANA, Maximo = 3, Momento = "Recordatorio" }, true, AHORA, "Carlos");

            Assert.AreEqual(ReglaMomentoPushCampana.AVISO_SIN_REGISTRO, r.Respuesta.Aviso);
            Assert.AreEqual(0, r.Respuesta.Mensajes.Single().QuitadasYaAnunciadas);
            Assert.AreEqual(3, r.Respuesta.Mensajes.Single().Productos.Count);
        }

        [TestMethod]
        public async Task Servicio_Cierre_PrimeroLoQueMiro_ConSuTexto_YElRegistroConElMotivoNuevo()
        {
            DateTime manana = new DateTime(2026, 11, 29, 11, 0, 0); // acaba el 30
            IFuentePushCampana fuente = Fuente(Registro(
                new InteresOfertaCliente { Producto = "5", VistaEl = AHORA, AbiertaEl = AHORA, AnunciadaPorPush = true },
                new InteresOfertaCliente { Producto = "3", AnunciadaPorPush = true }));
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).Returns(1);
            IRegistroOfertasMostradas registro = A.Fake<IRegistroOfertasMostradas>();
            var registradas = new List<FilaOfertaMostrada>();
            A.CallTo(() => registro.Servidas(A<IReadOnlyList<FilaOfertaMostrada>>._)).Invokes((IReadOnlyList<FilaOfertaMostrada> f) => registradas.AddRange(f));

            ResultadoPushCampana r = await Servicio(fuente, push, registro)
                .Ejecutar(new PeticionCampanaPushDTO { Campana = CAMPANA, Maximo = 3, Momento = "Cierre" }, false, manana, "Carlos");

            MensajePushCampanaDTO m = r.Respuesta.Mensajes.Single();
            Assert.AreEqual("5", m.Productos[0], "Lo que abrió va delante aunque ya se le anunciase");
            Assert.IsFalse(m.Productos.Contains("3"), "Lo anunciado que no miró, fuera");
            Assert.AreEqual(MotivosOfertaParaTi.LO_MIRASTE, m.Ofertas[0].Motivo);
            Assert.AreEqual("Rebajas de Primavera: termina mañana", m.Titulo);
            Assert.AreEqual("Producto 5 sigue al −30 % hasta mañana.", m.Cuerpo);
            A.CallTo(() => push.EnviarConsejoACliente("1", "1000", A<NotificacionPushDTO>.That.Matches(n =>
                n.Datos["momento"] == "Cierre" && n.Datos["tipo"] == "ofertas" && n.Datos["campana"] == CAMPANA && !n.Datos.ContainsKey("productos"))))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(MotivosOfertaParaTi.LO_MIRASTE, registradas.Single(f => f.Producto == "5").Motivo);
            Assert.AreEqual(OrigenesOfertaMostrada.PUSH, registradas[0].Origen);
            A.CallTo(() => fuente.Anotar(A<string>.That.Matches(s => s.Contains("\"Momento\":\"Cierre\"")), "Carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Servicio_CierreFueraDePlazo_400SinEnviar_TambienEnEnsayo()
        {
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            ServicioPushCampana servicio = Servicio(Fuente(), push);
            ResultadoPushCampana r = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = CAMPANA, Momento = "Cierre" }, true, AHORA, "Carlos");

            Assert.AreEqual(EstadoPushCampana.PeticionIncorrecta, r.Estado);
            StringAssert.Contains(r.Mensaje, "acaba hoy o mañana");
            StringAssert.Contains(await servicio.ValidarAntesDeEncolar(new PeticionCampanaPushDTO { Campana = CAMPANA, Momento = "Cierre" }, AHORA), "acaba hoy o mañana");
            Assert.IsNull(await servicio.ValidarAntesDeEncolar(new PeticionCampanaPushDTO { Campana = CAMPANA, Momento = "Cierre" }, new DateTime(2026, 11, 30, 10, 0, 0)));
            Assert.IsNull(await servicio.ValidarAntesDeEncolar(new PeticionCampanaPushDTO { Campana = CAMPANA, Momento = "Recordatorio" }, AHORA));
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        // ================================================================ el controlador

        private CampanasPushController Controlador(ServicioPushCampana servicio, DateTime ahora, List<string> encolados = null)
            => new CampanasPushController(servicio, (p, u) => { encolados?.Add(p.Momento ?? "(sin momento)"); return "42"; })
            {
                RequestContext = new HttpRequestContext { Principal = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Carlos", "Bearer"), new[] { "Dirección" }) },
                Ahora = () => ahora
            };

        [TestMethod]
        public async Task Controlador_CierreFueraDePlazo_400SinEncolar_YEnPlazo202()
        {
            var encolados = new List<string>();
            ServicioPushCampana servicio = Servicio(Fuente());

            IHttpActionResult fuera = await Controlador(servicio, AHORA, encolados).PostPush(new PeticionCampanaPushDTO { Campana = CAMPANA, Momento = "Cierre" }, false);
            StringAssert.Contains(((BadRequestErrorMessageResult)fuera).Message, "acaba hoy o mañana");
            Assert.AreEqual(0, encolados.Count);

            IHttpActionResult enPlazo = await Controlador(servicio, new DateTime(2026, 11, 30, 10, 0, 0), encolados)
                .PostPush(new PeticionCampanaPushDTO { Campana = CAMPANA, Momento = "Cierre" }, false);
            Assert.AreEqual(HttpStatusCode.Accepted, ((NegotiatedContentResult<PushCampanaEncoladaDTO>)enPlazo).StatusCode);
            CollectionAssert.AreEqual(new[] { "Cierre" }, encolados, "El momento viaja en la petición al trabajo de Hangfire");

            IHttpActionResult desconocido = await Controlador(servicio, AHORA, encolados).PostPush(new PeticionCampanaPushDTO { Campana = CAMPANA, Momento = "Final" }, false);
            Assert.AreEqual(ReglaMomentoPushCampana.MENSAJE_MOMENTO_DESCONOCIDO, ((BadRequestErrorMessageResult)desconocido).Message);
        }

        [TestMethod]
        public async Task Controlador_Ensayo_ConLosCamposNuevos_YLosDeSiempre()
        {
            IFuentePushCampana fuente = Fuente(Registro(new InteresOfertaCliente { Producto = "3", AnunciadaPorPush = true }));
            IHttpActionResult r = await Controlador(Servicio(fuente), AHORA).PostPush(new PeticionCampanaPushDTO { Campana = CAMPANA, Maximo = 2, Momento = "Recordatorio" });

            RespuestaCampanaPushDTO respuesta = ((OkNegotiatedContentResult<RespuestaCampanaPushDTO>)r).Content;
            JObject json = JObject.Parse(JsonConvert.SerializeObject(respuesta));
            foreach (string campo in new[] { "Ensayo", "Campana", "Momento", "Fin", "Aviso", "Destinatarios", "ConOfertas", "SinOfertas", "ConsultasPrecio", "ProductosPuntuados", "Segundos", "Saltados", "Mensajes" })
            {
                Assert.IsTrue(json.ContainsKey(campo), campo);
            }
            JObject mensaje = (JObject)json["Mensajes"][0];
            foreach (string campo in new[] { "Cliente", "Titulo", "Cuerpo", "Productos", "ImagenUrl", "Enviados", "Momento", "Ofertas", "QuitadasYaAnunciadas" })
            {
                Assert.IsTrue(mensaje.ContainsKey(campo), campo);
            }
            JObject oferta = (JObject)mensaje["Ofertas"][0];
            foreach (string campo in new[] { "Producto", "Nombre", "Motivo", "Texto", "Descuento" })
            {
                Assert.IsTrue(oferta.ContainsKey(campo), campo);
            }
            Assert.IsTrue((bool)json["Ensayo"]);
            Assert.AreEqual("Recordatorio", (string)mensaje["Momento"]);
            Assert.AreEqual(1, (int)mensaje["QuitadasYaAnunciadas"]);
        }
    }
}
