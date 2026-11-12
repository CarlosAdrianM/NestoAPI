using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#857: la entrega estimada que anuncia el correo, el disponible que sale de ella y los envíos nuestros de salida en copia.
    /// Los dos casos «reales» (UPS y GLS) llevan el texto tal como llegó; los demás correos son INVENTADOS.
    /// </summary>
    [TestClass]
    public class CorreosSeguimientoEntregaEstimadaTests
    {
        private static readonly DateTime CORREO = new DateTime(2026, 11, 12, 18, 30, 0); // jueves
        private static readonly TimeSpan CORTE = new TimeSpan(11, 0, 0);

        private const string UPS_TEXTO = @"¡Tu paquete está de camino!
De MBE STORE C/O MULTIBOXES S.L

Entrega estimada
Lunes 16/11/2026
entre 10:45 - 14:45

Cambiar entrega

UPS Standard®
1Z21E7X76890089163";

        private const string UPS_HTML = "<html><body><table><tr><td><h2>¡Tu paquete está de camino!</h2></td></tr>"
            + "<tr><td>De MBE STORE C/O MULTIBOXES S.L</td></tr>"
            + "<tr><td style=\"font-size:12px\">Entrega estimada</td></tr>"
            + "<tr><td><table><tr><td><strong>Lunes&nbsp;16/11/2026</strong></td></tr><tr><td>entre&nbsp;10:45&nbsp;-&nbsp;14:45</td></tr></table></td></tr>"
            + "<tr><td><a href=\"https://www.ups.com/track?loc=es_ES&amp;tracknum=1Z21E7X76890089163\">Cambiar entrega</a></td></tr>"
            + "<tr><td>UPS Standard&reg;<br>1Z21E7X76890089163</td></tr></table></body></html>";

        private const string GLS_TEXTO = @"¡Hola, Amazon Zaz1 - Zaragoza, Aragon!

Tu pedido 29440/930508 de NUEVA VISION S.A con Nº de seguimiento GLS 1378206148 está en camino.

Te lo entregaremos, como máximo, el 13/11/2026.

Datos destino:
     PLAT. LOG. ZARAGOZA ALI-28 Y ALI-2, 50197, ZARAGOZA.";

        private static CorreoCompras Correo(string cuerpo, bool html, string remitente, string asunto = "Su envío") => new CorreoCompras
        {
            Id = "AAA",
            MensajeId = "<" + Guid.NewGuid() + "@ejemplo.test>",
            Recibido = CORREO,
            RemitenteDireccion = remitente,
            Asunto = asunto,
            Cuerpo = cuerpo,
            CuerpoEsHtml = html
        };

        private static EntregaEstimada Extraer(string texto) => ExtractorEntregaEstimada.Extraer(texto, CORREO);

        private static void Tramo(EntregaEstimada e, int dia, int mes, int anio, string desde, string hasta)
        {
            Assert.IsNotNull(e, "Tendría que haber sacado la entrega");
            Assert.AreEqual(new DateTime(anio, mes, dia), e.Dia);
            Assert.AreEqual(desde == null ? (TimeSpan?)null : TimeSpan.Parse(desde), e.Desde, "Desde");
            Assert.AreEqual(hasta == null ? (TimeSpan?)null : TimeSpan.Parse(hasta), e.Hasta, "Hasta");
        }

        #region Extractor

        [TestMethod]
        public void Ups_TextoPlano_DiaYTramo()
        {
            Tramo(Extraer(UPS_TEXTO), 16, 11, 2026, "10:45", "14:45");
        }

        [TestMethod]
        public void Ups_HtmlConTablaYEspaciosDuros_DiaYTramo()
        {
            CorreoCompras correo = Correo(UPS_HTML, true, "pkginfo@ups.com");

            Tramo(Extraer(correo.Texto), 16, 11, 2026, "10:45", "14:45");
        }

        [TestMethod]
        public void Ups_EtiquetaYFechaEnCeldasDeLaMismaFila()
        {
            string html = "<table><tr><td>Entrega estimada:</td><td>lunes, 16/11/2026</td><td>de 10:45 a 14:45</td></tr></table>";

            Tramo(Extraer(TextoCorreo.HtmlATexto(html)), 16, 11, 2026, "10:45", "14:45");
        }

        [TestMethod]
        public void Gls_FraseComoMaximo_SoloDia()
        {
            Tramo(Extraer(GLS_TEXTO), 13, 11, 2026, null, null);
        }

        [TestMethod]
        public void FechaSinTramo()
        {
            Tramo(Extraer("Fecha prevista de entrega: 17-11-2026\nGracias por su compra."), 17, 11, 2026, null, null);
        }

        [DataTestMethod]
        [DataRow("Entrega estimada: 16/11/2026 entre 10:45 - 14:45")]
        [DataRow("Entrega estimada: 16/11/2026, de 10:45 a 14:45")]
        [DataRow("Entrega estimada: 16.11.2026 10:45-14:45")]
        [DataRow("Estimated delivery: 16/11/26 between 10:45 and 14:45")]
        [DataRow("Scheduled delivery: Monday 16/11/2026 from 10:45 to 14:45")]
        [DataRow("ENTREGA PREVISTA PARA EL 16/11/2026 ENTRE LAS 10:45 Y LAS 14:45")]
        public void Tramo_EnCadaFormato(string texto)
        {
            Tramo(Extraer(texto), 16, 11, 2026, "10:45", "14:45");
        }

        [DataTestMethod]
        [DataRow("Fecha estimada de entrega: 18/11/2026 antes de las 14:00")]
        [DataRow("Delivery date: 18/11/2026 before 14:00")]
        public void UnaSolaHora_SoloHasta(string texto)
        {
            Tramo(Extraer(texto), 18, 11, 2026, null, "14:00");
        }

        [DataTestMethod]
        [DataRow("Su pedido llegará el miércoles 18 de noviembre de 2026.")]
        [DataRow("Fecha de entrega: 18 noviembre 2026")]
        [DataRow("Será entregado el 18 de noviembre.")]
        [DataRow("Your parcel will be delivered on November 18, 2026.")]
        public void FechaEnLetras(string texto)
        {
            Tramo(Extraer(texto), 18, 11, 2026, null, null);
        }

        [TestMethod]
        public void SinAnio_QueQuedariaMuyAtras_EsDelAnioSiguiente()
        {
            // Correo de finales de diciembre que anuncia el 5 de enero
            EntregaEstimada e = ExtractorEntregaEstimada.Extraer("Entrega prevista: 5 de enero", new DateTime(2026, 12, 29));

            Tramo(e, 5, 1, 2027, null, null);
        }

        [DataTestMethod]
        [DataRow("Tu pedido 16/11/2026 está en camino.")]
        [DataRow("Fecha del pedido: 16/11/2026")]
        [DataRow("Hemos recibido su pedido el 16/11/2026 a las 10:45.")]
        public void SinEtiqueta_Nada(string texto)
        {
            Assert.IsNull(Extraer(texto));
        }

        [DataTestMethod]
        [DataRow("Entrega estimada: 11/11/2026")]
        [DataRow("Entrega estimada: 15/01/2027")]
        [DataRow("Entrega estimada: 31/02/2026")]
        public void FechaFueraDeRangoOImposible_Nada(string texto)
        {
            Assert.IsNull(Extraer(texto));
        }

        [TestMethod]
        public void LaFechaMuyLejosDeLaEtiqueta_NoCuenta()
        {
            Assert.IsNull(Extraer("Entrega estimada\n\n\n\n\n\nGracias.\nAlgo más\n16/11/2026"));
        }

        #endregion

        #region Disponible

        private static readonly Func<DateTime, bool> SinFestivos = _ => false;

        [TestMethod]
        public void Disponible_AntesDelCorte_EseMismoDia()
        {
            Assert.AreEqual(new DateTime(2026, 11, 16), ReglaDisponibleCompra.Calcular(new DateTime(2026, 11, 16), new TimeSpan(10, 30, 0), CORTE, SinFestivos));
        }

        [TestMethod]
        public void Disponible_JustoEnElCorte_ElSiguienteLaborable()
        {
            Assert.AreEqual(new DateTime(2026, 11, 17), ReglaDisponibleCompra.Calcular(new DateTime(2026, 11, 16), CORTE, CORTE, SinFestivos));
        }

        [TestMethod]
        public void Disponible_DespuesDelCorte_ElSiguienteLaborable()
        {
            // El caso de UPS: lunes 16/11 10:45-14:45 → martes 17/11 (manda el final del tramo)
            Assert.AreEqual(new DateTime(2026, 11, 17), ReglaDisponibleCompra.Calcular(
                new EntregaEstimada { Dia = new DateTime(2026, 11, 16), Desde = new TimeSpan(10, 45, 0), Hasta = new TimeSpan(14, 45, 0) }, CORTE, SinFestivos));
        }

        [TestMethod]
        public void Disponible_SinHora_ElSiguienteLaborable()
        {
            Assert.AreEqual(new DateTime(2026, 11, 17), ReglaDisponibleCompra.Calcular(new DateTime(2026, 11, 16), null, CORTE, SinFestivos));
        }

        [TestMethod]
        public void Disponible_Viernes_ElLunes()
        {
            Assert.AreEqual(new DateTime(2026, 11, 16), ReglaDisponibleCompra.Calcular(new DateTime(2026, 11, 13), null, CORTE, SinFestivos));
        }

        [TestMethod]
        public void Disponible_SabadoAunqueSeaPronto_ElLunes()
        {
            Assert.AreEqual(new DateTime(2026, 11, 16), ReglaDisponibleCompra.Calcular(new DateTime(2026, 11, 14), new TimeSpan(9, 0, 0), CORTE, SinFestivos));
        }

        [TestMethod]
        public void Disponible_VisperaDeFestivo_SeSaltaElFestivo()
        {
            Func<DateTime, bool> inmaculada = d => d == new DateTime(2026, 12, 8);

            Assert.AreEqual(new DateTime(2026, 12, 9), ReglaDisponibleCompra.Calcular(new DateTime(2026, 12, 7), new TimeSpan(14, 0, 0), CORTE, inmaculada));
        }

        [TestMethod]
        public void Disponible_LlegaUnFestivoPorLaManana_ElSiguienteLaborable()
        {
            Func<DateTime, bool> inmaculada = d => d == new DateTime(2026, 12, 8);

            Assert.AreEqual(new DateTime(2026, 12, 9), ReglaDisponibleCompra.Calcular(new DateTime(2026, 12, 8), new TimeSpan(9, 0, 0), CORTE, inmaculada));
        }

        #endregion

        #region Salida nuestra

        [TestMethod]
        public void Salida_ElAvisoDeGls_EsNuestra()
        {
            Assert.IsTrue(EnvioNuestroDeSalida.Es(GLS_TEXTO));
        }

        [DataTestMethod]
        [DataRow("Remitente: NUEVA VISIÓN, S.A.")]
        [DataRow("Su paquete enviado por Nueva Vision S.A. llegará mañana.")]
        [DataRow("You have a parcel from NUEVA VISION SA")]
        [DataRow("Su envío 4455 de Nueva Visión está en reparto.")]
        public void Salida_NosPonenDeRemitente(string texto)
        {
            Assert.IsTrue(EnvioNuestroDeSalida.Es(texto));
        }

        [DataTestMethod]
        [DataRow("¡Hola, Nueva Visión! Tu paquete está de camino.")]
        [DataRow("Destinatario: NUEVA VISION S.A.\nRemitente: COSMÉTICOS INVENTADOS S.L.")]
        [DataRow("Estimado cliente NUEVA VISION, su pedido ha sido enviado.")]
        [DataRow("Pedido 1234 de NUEVA VISION S.A. preparado y enviado.")]
        [DataRow("From: Compras Nueva Vision <compras@ejemplo.test>\nSent: jueves")]
        public void Salida_NombrarnosComoDestinatario_NoCuenta(string texto)
        {
            Assert.IsFalse(EnvioNuestroDeSalida.Es(texto));
        }

        #endregion

        #region Analizador y guardado

        [TestMethod]
        public void Analizar_Ups_AgenciaCodigoYEntrega()
        {
            AnalisisCorreoSeguimiento analisis = AnalizadorCorreoSeguimiento.Analizar(
                Correo(UPS_TEXTO, false, "pkginfo@ups.com", "Se ha enviado tu paquete"), AgenciasEntradaRegistradas.Todas, null);

            Assert.IsTrue(analisis.Guardar);
            Assert.AreEqual("UPS", analisis.Agencia);
            Assert.AreEqual("1Z21E7X76890089163", analisis.Seguimientos.First().Codigo);
            Tramo(analisis.EntregaEstimada, 16, 11, 2026, "10:45", "14:45");
            Assert.IsFalse(analisis.EsSalidaNuestra, "«De MBE STORE…» es el remitente del paquete: es una entrada");
        }

        [TestMethod]
        public void Analizar_Gls_SalidaNuestraConSuEntrega()
        {
            AnalisisCorreoSeguimiento analisis = AnalizadorCorreoSeguimiento.Analizar(
                Correo(GLS_TEXTO, false, "no-reply@gls-spain.com", "Tu pedido está en camino"), AgenciasEntradaRegistradas.Todas, null);

            Assert.IsTrue(analisis.Guardar);
            Assert.AreEqual("GLS", analisis.Agencia);
            Assert.AreEqual("1378206148", analisis.Seguimientos.Single().Codigo);
            Tramo(analisis.EntregaEstimada, 13, 11, 2026, null, null);
            Assert.IsTrue(analisis.EsSalidaNuestra);
        }

        private static (ServicioCorreosSeguimientoCompras servicio, IRepositorioCorreosSeguimiento repositorio, List<CorreoSeguimientoFila> insertadas) Nucleo()
        {
            var insertadas = new List<CorreoSeguimientoFila>();
            IRepositorioCorreosSeguimiento repositorio = A.Fake<IRepositorioCorreosSeguimiento>();
            A.CallTo(() => repositorio.LeerRemitentes()).Returns(new List<RemitenteMarcado>());
            A.CallTo(() => repositorio.Insertar(A<CorreoSeguimientoFila>._)).ReturnsLazily((CorreoSeguimientoFila f) =>
            {
                insertadas.Add(f);
                return Task.FromResult(true);
            });
            var servicio = new ServicioCorreosSeguimientoCompras(A.Fake<ILectorBuzonCompras>(), repositorio, null, null, "1",
                () => CORTE, _ => false);
            return (servicio, repositorio, insertadas);
        }

        private static List<PedidoCompraEnAire> PedidosConGls() => new List<PedidoCompraEnAire>
        {
            // Proveedor inventado cuyo nombre sale en los dos correos («MBE STORE»), para que hubiera candidatos si se asignara
            new PedidoCompraEnAire { Empresa = "1", Pedido = 930508, Proveedor = "9001", NombreProveedor = "NUEVA VISION S.A" },
            new PedidoCompraEnAire { Empresa = "1", Pedido = 912001, Proveedor = "9002", NombreProveedor = "MBE STORE" }
        };

        [TestMethod]
        public async Task Guardar_Gls_SalidaNuestraSinAsignacionNiDisponible()
        {
            var (servicio, _, insertadas) = Nucleo();
            int pedidosPedidos = 0;

            bool todo = await servicio.GuardarCorreos(new List<CorreoCompras> { Correo(GLS_TEXTO, false, "no-reply@gls-spain.com") },
                new ResultadoPasadaCorreosSeguimiento(), _ => { pedidosPedidos++; return Task.FromResult(PedidosConGls()); });

            Assert.IsTrue(todo);
            CorreoSeguimientoFila fila = insertadas.Single();
            Assert.AreEqual("GLS", fila.Agencia);
            Assert.AreEqual("1378206148", fila.Codigo);
            Assert.AreEqual(true, fila.EsSalidaNuestra);
            Assert.AreEqual(new DateTime(2026, 11, 13), fila.EntregaEstimadaDia);
            Assert.IsNull(fila.EntregaEstimadaDesde);
            Assert.IsNull(fila.EntregaEstimadaHasta);
            Assert.IsNull(fila.DisponibleEstimado, "Una salida nuestra no tiene disponible");
            Assert.IsNull(fila.ProveedorPropuesto);
            Assert.IsNull(fila.PedidosCandidatos, "Sin candidatos (y así tampoco entra en la medición)");
            Assert.AreEqual(0, pedidosPedidos, "Ni siquiera se piden los pedidos en el aire");
        }

        [TestMethod]
        public async Task Guardar_Ups_EntradaConEntregaYDisponible()
        {
            var (servicio, _, insertadas) = Nucleo();

            _ = await servicio.GuardarCorreos(new List<CorreoCompras> { Correo(UPS_TEXTO, false, "pkginfo@ups.com", "Se ha enviado tu paquete") },
                new ResultadoPasadaCorreosSeguimiento(), _ => Task.FromResult(PedidosConGls()));

            CorreoSeguimientoFila fila = insertadas.Single();
            Assert.AreEqual(false, fila.EsSalidaNuestra);
            Assert.AreEqual(new DateTime(2026, 11, 16), fila.EntregaEstimadaDia);
            Assert.AreEqual(new TimeSpan(10, 45, 0), fila.EntregaEstimadaDesde);
            Assert.AreEqual(new TimeSpan(14, 45, 0), fila.EntregaEstimadaHasta);
            Assert.AreEqual(new DateTime(2026, 11, 17), fila.DisponibleEstimado);
        }

        [TestMethod]
        public void Sql_InsertarComponeLasColumnasOpcionales()
        {
            string ninguna = RepositorioCorreosSeguimiento.SqlInsertar(false, false);
            string todas = RepositorioCorreosSeguimiento.SqlInsertar(true, true);
            string soloEntrega = RepositorioCorreosSeguimiento.SqlInsertar(false, true);

            Assert.IsFalse(ninguna.Contains("NumAdjuntos") || ninguna.Contains("EntregaEstimadaDia"));
            StringAssert.Contains(todas, "FechaCaptura, NumAdjuntos, Adjuntos, EntregaEstimadaDia, EntregaEstimadaDesde, EntregaEstimadaHasta, DisponibleEstimado, EsSalidaNuestra)");
            StringAssert.Contains(todas, "GETDATE(), @numAdjuntos, @adjuntos, @entregaDia, @entregaDesde, @entregaHasta, @disponible, @esSalida");
            Assert.IsFalse(soloEntrega.Contains("NumAdjuntos"));
            StringAssert.Contains(soloEntrega, "GETDATE(), @entregaDia");
            Assert.AreEqual(RepositorioCorreosSeguimiento.SqlInsertar(true, false), RepositorioCorreosSeguimiento.SQL_INSERTAR_CON_ADJUNTOS);
        }

        [TestMethod]
        public void Sql_LasLecturasLlevanSiempreLasColumnasDeLaEntrega()
        {
            // Sin ellas SqlQuery<CorreoSeguimientoFila> fallaría
            StringAssert.Contains(RepositorioCorreosSeguimiento.SQL_PENDIENTES_MEDIR, "AS EsSalidaNuestra");
            StringAssert.Contains(RepositorioCorreosSeguimiento.SQL_PENDIENTES_MEDIR_POR_ID, "AS EsSalidaNuestra");
            StringAssert.Contains(string.Format(RepositorioCorreosSeguimiento.SQL_CORREOS, RepositorioCorreosSeguimiento.COLUMNAS_ADJUNTOS_NULAS,
                RepositorioCorreosSeguimiento.COLUMNAS_ENTREGA_NULAS), "CAST(NULL AS time(0)) AS EntregaEstimadaHasta");
        }

        #endregion
    }
}
