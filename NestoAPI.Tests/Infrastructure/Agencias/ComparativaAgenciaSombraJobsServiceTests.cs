using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Job que rellena ComparativaAgenciaSombra. Se fakea NVEntities con DbSets en memoria (sync).
    /// Usa las tarifas reales (GLS vs CTT sombra): en Peninsular CTT es más barata, así que la sombra
    /// gana. No se asignan importes exactos; se valida la mecánica (fila correcta, idempotencia, filtros).
    /// </summary>
    [TestClass]
    public class ComparativaAgenciaSombraJobsServiceTests
    {
        private NVEntities db;
        private List<AgenciaTransporte> agencias;
        private List<EnviosAgencia> envios;
        private List<ComparativaAgenciaSombra> comparativas;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            agencias = new List<AgenciaTransporte>
            {
                new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "GLS", RecargoCombustible = 0.1055m, EsSombra = false },
                new AgenciaTransporte { Numero = 13, Empresa = "1  ", Nombre = "CTT", RecargoCombustible = 0m, EsSombra = true }
            };
            envios = new List<EnviosAgencia>();
            comparativas = new List<ComparativaAgenciaSombra>();

            A.CallTo(() => db.AgenciasTransportes).Returns(FakeSet(agencias));
            A.CallTo(() => db.EnviosAgencias).Returns(FakeSet(envios));
            A.CallTo(() => db.ComparativaAgenciaSombras).Returns(FakeSet(comparativas));
            A.CallTo(() => db.SaveChanges()).Returns(1);
        }

        private EnviosAgencia EnvioPeninsular(int numero, short estado, decimal peso, decimal reembolso = 0m, int agencia = 1)
            => new EnviosAgencia
            {
                Numero = numero,
                Empresa = "1  ",
                Pedido = 90000 + numero,
                Agencia = agencia,
                Estado = estado,
                Peso = peso,
                Reembolso = reembolso,
                CodPostal = "08001", // Barcelona -> Peninsular
                Fecha = DateTime.Today
            };

        [TestMethod]
        public void RegistrarComparativas_EnvioPeninsular_RegistraLaSombraComoGanadora()
        {
            envios.Add(EnvioPeninsular(100, Constantes.Agencias.ESTADO_EN_CURSO, peso: 3m));

            int insertados = new ComparativaAgenciaSombraJobsService(db).RegistrarComparativas(30);

            Assert.AreEqual(1, insertados);
            Assert.AreEqual(1, comparativas.Count);
            var fila = comparativas.Single();
            Assert.AreEqual(100, fila.NumeroEnvio);
            Assert.AreEqual(1, fila.AgenciaRealId);
            Assert.AreEqual(13, fila.AgenciaSombraId);             // CTT
            Assert.IsTrue(fila.SombraGana, "En Peninsular CTT es más barata que GLS.");
            Assert.IsTrue(fila.Ahorro > 0);
            Assert.AreEqual("Peninsular", fila.Zona);
        }

        [TestMethod]
        public void RegistrarComparativas_SaltaPendientesYSinPeso()
        {
            envios.Add(EnvioPeninsular(101, Constantes.Agencias.ESTADO_PENDIENTE, peso: 3m)); // pendiente
            envios.Add(EnvioPeninsular(102, Constantes.Agencias.ESTADO_EN_CURSO, peso: 0m));   // sin peso

            int insertados = new ComparativaAgenciaSombraJobsService(db).RegistrarComparativas(30);

            Assert.AreEqual(0, insertados);
            Assert.AreEqual(0, comparativas.Count);
        }

        [TestMethod]
        public void RegistrarComparativas_EsIdempotente()
        {
            envios.Add(EnvioPeninsular(100, Constantes.Agencias.ESTADO_EN_CURSO, peso: 3m));
            comparativas.Add(new ComparativaAgenciaSombra { NumeroEnvio = 100 }); // ya registrado

            int insertados = new ComparativaAgenciaSombraJobsService(db).RegistrarComparativas(30);

            Assert.AreEqual(0, insertados, "Un envío ya registrado no se vuelve a insertar.");
            Assert.AreEqual(1, comparativas.Count);
        }

        [TestMethod]
        public void RegistrarComparativas_SinAgenciasSombra_NoHaceNada()
        {
            agencias.Single(a => a.Numero == 13).EsSombra = false; // ninguna sombra
            envios.Add(EnvioPeninsular(100, Constantes.Agencias.ESTADO_EN_CURSO, peso: 3m));

            int insertados = new ComparativaAgenciaSombraJobsService(db).RegistrarComparativas(30);

            Assert.AreEqual(0, insertados);
            Assert.AreEqual(0, comparativas.Count);
        }

        // Fake de DbSet en memoria, solo síncrono (el job no usa async), con enumeración perezosa
        // para soportar múltiples recorridos y reflejar los Add.
        private static DbSet<T> FakeSet<T>(List<T> data) where T : class
        {
            var set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>());
            A.CallTo(() => ((IQueryable<T>)set).Provider).ReturnsLazily(() => data.AsQueryable().Provider);
            A.CallTo(() => ((IQueryable<T>)set).Expression).ReturnsLazily(() => data.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            A.CallTo(() => set.Add(A<T>._)).Invokes((T x) => data.Add(x)).ReturnsLazily((T x) => x);
            return set;
        }
    }
}
