using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Caso 251099 (Carlos 19/11/26): al ENTREGAR un envío, sus bultos que sigan en la furgoneta de quien entrega se dan por entregados
    /// aunque no se hayan leído (marcados y con rastro); la red de las 22:00 cierra los de envíos que ya constan entregados.
    /// </summary>
    public partial class FurgonetaBultosTests
    {
        private const int ENVIO_251099 = 251099;

        private async Task CargarTodos(int envio, int bultos, string usuario)
        {
            for (int b = 1; b <= bultos; b++)
            {
                var r = await servicio.Cargar(Carga($"RP{envio}-{b}"), usuario);
                Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            }
        }

        [TestMethod]
        public async Task Caso251099_Leyendo5De25_NoQuedaNingunoEnLaFurgoneta()
        {
            Envio(ENVIO_251099, bultos: 25);
            await CargarTodos(ENVIO_251099, 25, MIGUEL);

            var r = await servicio.Entregar(ENVIO_251099, Entrega(null, Enumerable.Range(1, 5).Select(b => $"RP{ENVIO_251099}-{b}").ToArray()), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(25, r.Contenido.BultosEntregados);
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count, "Ni uno sigue en la furgoneta de Miguel");
            List<MovimientoBultoReparto> entregados = furgoneta.Filas.Where(f => f.Tipo == TiposMovimientoBulto.ENTREGADO).ToList();
            Assert.AreEqual(25, entregados.Count);
            Assert.AreEqual(5, entregados.Count(f => !f.Descripcion.EndsWith(CierreEntregaFurgoneta.MARCA, StringComparison.Ordinal)), "Los leídos, sin marca");
            Assert.AreEqual(20, entregados.Count(f => f.Descripcion.EndsWith(CierreEntregaFurgoneta.MARCA, StringComparison.Ordinal)), "Los otros 20, «sin leer»");
            var rastro = furgoneta.Rastros.Single();
            Assert.AreEqual(CierreEntregaFurgoneta.TABLA_MODIFICACIONES, rastro.Tabla);
            StringAssert.Contains(rastro.Nuevo, $"RP{ENVIO_251099}-25");
            Assert.IsFalse(rastro.Nuevo.Contains($"\"SinLeer\":[\"RP{ENVIO_251099}-1\""), "Los leídos no van como sin leer");
        }

        [TestMethod]
        public async Task Caso251099_ReintentoConLaMismaClave_NiDuplicaNiOtroRastro()
        {
            Envio(ENVIO_251099, bultos: 4);
            await CargarTodos(ENVIO_251099, 4, MIGUEL);
            Guid clave = Guid.NewGuid();

            _ = await servicio.Entregar(ENVIO_251099, Entrega(clave, $"RP{ENVIO_251099}-1"), MIGUEL);
            int filas = furgoneta.Filas.Count;
            _ = await servicio.Entregar(ENVIO_251099, Entrega(clave, $"RP{ENVIO_251099}-1"), MIGUEL);

            Assert.AreEqual(filas, furgoneta.Filas.Count);
            Assert.AreEqual(1, furgoneta.Rastros.Count);
        }

        [TestMethod]
        public async Task Caso251099_LosQueLlevaOtroRepartidor_NoSeTocan()
        {
            Envio(ENVIO_251099, bultos: 3);
            _ = await servicio.Cargar(Carga($"RP{ENVIO_251099}-1"), MIGUEL);
            _ = await servicio.Cargar(Carga($"RP{ENVIO_251099}-2"), MIGUEL);
            MovimientoBultoReparto deAndre = Mov(1000, $"RP{ENVIO_251099}-3", TiposMovimientoBulto.CARGADO, ANDRE, AHORA.AddMinutes(-5));
            deAndre.EnvioId = ENVIO_251099;
            furgoneta.Filas.Add(deAndre);
            Assert.AreEqual(1, (await LoQueLleva(ANDRE)).Count, "Precondición: Andre lleva el 3");

            var r = await servicio.Entregar(ENVIO_251099, Entrega(null, $"RP{ENVIO_251099}-1"), MIGUEL);

            Assert.AreEqual(2, r.Contenido.BultosEntregados);
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
            CollectionAssert.AreEqual(new[] { $"RP{ENVIO_251099}-3" }, (await LoQueLleva(ANDRE)).Select(b => b.Codigo).ToList(), "El de Andre lo cierra el job de las 22:00");
        }

        [TestMethod]
        public async Task Caso251099_ConLecturaObligatoria_UnoLeidoBasta_YLosDemasSeCierran()
        {
            Envio(ENVIO_251099, bultos: 3);
            await CargarTodos(ENVIO_251099, 3, MIGUEL);
            parametros.Obligatorio = "1";

            var sinLeer = await servicio.Entregar(ENVIO_251099, Entrega(), MIGUEL);
            var uno = await servicio.Entregar(ENVIO_251099, Entrega(null, $"RP{ENVIO_251099}-2"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Invalido, sinLeer.Tipo, "Sin leer ninguno sigue sin valer");
            Assert.AreEqual(TipoResultadoReparto.Ok, uno.Tipo, uno.Mensaje);
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
        }

        [TestMethod]
        public async Task Caso251099_SinLeerNinguno_TambienMarcaYDejaRastro()
        {
            Envio(ENVIO_251099, bultos: 2);
            await CargarTodos(ENVIO_251099, 2, MIGUEL);

            _ = await servicio.Entregar(ENVIO_251099, Entrega(), MIGUEL);

            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
            Assert.IsTrue(furgoneta.Filas.Where(f => f.Tipo == TiposMovimientoBulto.ENTREGADO).All(f => f.Descripcion.EndsWith(CierreEntregaFurgoneta.MARCA, StringComparison.Ordinal)));
            Assert.AreEqual(1, furgoneta.Rastros.Count);
        }

        [TestMethod]
        public async Task Caso251099_Job22_CierraLosDeEnviosYaEntregados_YNoAvisaDeEllos()
        {
            // Lo que quedó ayer en la furgoneta de Miguel (20 sin leer) y un envío que sigue en reparto
            DateTime ayer = AHORA.Date.AddDays(-1).AddHours(11);
            for (int b = 1; b <= 20; b++)
            {
                MovimientoBultoReparto m = Mov(b, $"RP{ENVIO_251099}-{b + 5}", TiposMovimientoBulto.CARGADO, MIGUEL, ayer);
                m.EnvioId = ENVIO_251099;
                furgoneta.Filas.Add(m);
            }
            MovimientoBultoReparto vivo = Mov(100, "RP251300-1", TiposMovimientoBulto.CARGADO, MIGUEL, ayer);
            vivo.EnvioId = 251300;
            furgoneta.Filas.Add(vivo);
            var preguntados = new List<int>();
            var job = new FurgonetaSinDescargarJobsService(furgoneta, avisosFurgoneta, () => AHORA.Date.AddHours(22),
                enviosEntregados: envios => { preguntados.AddRange(envios); return Task.FromResult(new HashSet<int> { ENVIO_251099 }); });

            int avisados = await job.AvisarSinDescargar();
            int otraVez = await job.AvisarSinDescargar();

            CollectionAssert.AreEquivalent(new[] { ENVIO_251099, 251300, 251300 }, preguntados, "La segunda vez ya no pregunta por el entregado");
            Assert.AreEqual(1, avisados);
            Assert.AreEqual(1, otraVez);
            Assert.AreEqual("RP251300-1", avisosFurgoneta.Llamadas[0].Pendientes.Single().Codigo, "Solo avisa del envío que sigue en reparto");
            List<MovimientoBultoReparto> cerrados = furgoneta.Filas.Where(f => f.Tipo == TiposMovimientoBulto.ENTREGADO).ToList();
            Assert.AreEqual(20, cerrados.Count);
            Assert.IsTrue(cerrados.All(c => c.Origen == OrigenesMovimientoBulto.ALMACEN && c.Usuario == MIGUEL));
            Assert.IsTrue(cerrados.All(c => c.Descripcion.EndsWith(CierreEntregaFurgoneta.MARCA.Trim(), StringComparison.Ordinal)));
            Assert.AreEqual(1, furgoneta.Rastros.Count, "Un rastro por envío");
            Assert.AreEqual(0, NucleoFurgoneta.EnFurgoneta(furgoneta.Filas, MIGUEL).Count(m => m.EnvioId == ENVIO_251099));
        }

        [TestMethod]
        public async Task Caso251099_Job22_SiNoSePuedeMirarElEstado_AvisaComoSiempre()
        {
            MovimientoBultoReparto m = Mov(1, $"RP{ENVIO_251099}-1", TiposMovimientoBulto.CARGADO, MIGUEL, AHORA.Date.AddHours(9));
            m.EnvioId = ENVIO_251099;
            furgoneta.Filas.Add(m);
            var job = new FurgonetaSinDescargarJobsService(furgoneta, avisosFurgoneta, () => AHORA.Date.AddHours(22),
                enviosEntregados: _ => throw new InvalidOperationException("BD caída"));

            Assert.AreEqual(1, await job.AvisarSinDescargar());
            Assert.AreEqual(1, furgoneta.Filas.Count, "No se cierra nada");
        }

        [TestMethod]
        public void CierreEntregaFurgoneta_Marcar_NoPasaDeLaColumnaNiSeRepite()
        {
            string larga = new string('A', 200);

            string marcada = CierreEntregaFurgoneta.Marcar(larga);

            Assert.AreEqual(CierreEntregaFurgoneta.LONGITUD_DESCRIPCION, marcada.Length);
            Assert.IsTrue(marcada.EndsWith(CierreEntregaFurgoneta.MARCA, StringComparison.Ordinal));
            Assert.AreEqual(marcada, CierreEntregaFurgoneta.Marcar(marcada));
            Assert.AreEqual("PELUQUERÍA ANA (MADRID) (entregado sin leer)", CierreEntregaFurgoneta.Marcar("PELUQUERÍA ANA (MADRID)"));
            Assert.AreEqual("(entregado sin leer)", CierreEntregaFurgoneta.Marcar(null));
        }
    }
}
