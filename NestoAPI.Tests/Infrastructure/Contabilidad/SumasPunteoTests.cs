using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Models.ApuntesBanco;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    /// <summary>
    /// 23/09/26: la ventana Bancos tardaba ~10 s en abrir porque el estado de punteo se preguntaba
    /// apunte a apunte (1.110 consultas para 555 apuntes de La Caixa). Ahora se suma todo agrupado.
    /// </summary>
    [TestClass]
    public class SumasPunteoTests
    {
        private static IQueryable<ConciliacionBancariaPunteo> Punteos(params ConciliacionBancariaPunteo[] punteos)
            => new TestDbAsyncEnumerable<ConciliacionBancariaPunteo>(punteos);

        [TestMethod]
        public async Task PorApunteBanco_SumaLosPunteosDeCadaApunteYSoloDeLosPedidos()
        {
            var punteos = Punteos(
                new ConciliacionBancariaPunteo { ApunteBancoId = 1, ImportePunteado = 60m },
                new ConciliacionBancariaPunteo { ApunteBancoId = 1, ImportePunteado = 40m },
                new ConciliacionBancariaPunteo { ApunteBancoId = 2, ImportePunteado = 10m },
                new ConciliacionBancariaPunteo { ApunteBancoId = 99, ImportePunteado = 5m },
                new ConciliacionBancariaPunteo { ApunteBancoId = null, ApunteContabilidadId = 1, ImportePunteado = 7m });

            Dictionary<int, decimal> sumas = await SumasPunteo.PorApunteBancoAsync(punteos, new[] { 1, 2, 3 });

            Assert.AreEqual(100m, SumasPunteo.SumaDe(sumas, 1));
            Assert.AreEqual(10m, SumasPunteo.SumaDe(sumas, 2));
            Assert.AreEqual(0m, SumasPunteo.SumaDe(sumas, 3), "Sin punteos = 0");
            Assert.IsFalse(sumas.ContainsKey(99), "Solo los apuntes pedidos");
        }

        [TestMethod]
        public async Task PorApunteContabilidad_NoMezclaLosPunteosDelBanco()
        {
            var punteos = Punteos(
                new ConciliacionBancariaPunteo { ApunteBancoId = 1, ApunteContabilidadId = 1, ImportePunteado = 30m },
                new ConciliacionBancariaPunteo { ApunteBancoId = 1, ApunteContabilidadId = null, ImportePunteado = 70m });

            Dictionary<int, decimal> sumas = await SumasPunteo.PorApunteContabilidadAsync(punteos, new[] { 1 });

            Assert.AreEqual(30m, SumasPunteo.SumaDe(sumas, 1));
        }

        [TestMethod]
        public async Task PorApunteBanco_MasIdsQueUnLote_LosSumaTodos()
        {
            var lista = Enumerable.Range(1, 1200)
                .Select(i => new ConciliacionBancariaPunteo { ApunteBancoId = i, ImportePunteado = 1m })
                .ToArray();

            Dictionary<int, decimal> sumas = await SumasPunteo.PorApunteBancoAsync(Punteos(lista), Enumerable.Range(1, 1200));

            Assert.AreEqual(1200, sumas.Count);
        }

        [TestMethod]
        public void EstadoPunteoContabilidad_ClasificaIgualQueAntes()
        {
            Assert.AreEqual(EstadoPunteo.SinPuntear, ContabilidadesController.EstadoPunteoContabilidad(0m, 50m));
            Assert.AreEqual(EstadoPunteo.CompletamentePunteado, ContabilidadesController.EstadoPunteoContabilidad(50m, 50m));
            Assert.AreEqual(EstadoPunteo.ParcialmentePunteado, ContabilidadesController.EstadoPunteoContabilidad(20m, 50m));
        }
    }
}
