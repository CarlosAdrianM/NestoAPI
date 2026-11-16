using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes.Embudo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// Embudo de Odoo (Carlos 16/11/26): los clientes de origen AMAZON o WEB (orígenes de canal, EsLead = 0, #848) entran en el
    /// bloque sea cual sea su vendedor y su estado (salvo de baja), siempre con alguna persona con correo y boletín y sin estar ya
    /// en el registro. Mismo orden que los demás (los más recientes primero) y mismo tope del bloque.
    /// </summary>
    [TestClass]
    public class EmbudoOrigenesTests
    {
        private static CandidatoEmbudo Candidato(string cliente, string origen = null, bool porOrigen = false, int personasConCorreo = 1,
            bool yaEnviado = false, bool pendiente = false) => new CandidatoEmbudo
            {
                Cliente = cliente,
                Contactos = 1,
                ContactosActivos = 1,
                PersonasConCorreo = personasConCorreo,
                Origen = origen,
                EntraPorOrigen = porOrigen,
                YaEnviado = yaEnviado,
                PendienteSincronizar = pendiente
            };

        private static IRepositorioClientesEmbudo Repositorio(params CandidatoEmbudo[] candidatos)
        {
            IRepositorioClientesEmbudo repo = A.Fake<IRepositorioClientesEmbudo>();
            A.CallTo(() => repo.HayTabla()).Returns(true);
            A.CallTo(() => repo.LeerCandidatos(A<string>._, A<IList<short>>._, A<bool>._)).Returns(candidatos.ToList());
            A.CallTo(() => repo.Registrar(A<string>._, A<IList<string>>._, A<string>._))
                .ReturnsLazily((string b, IList<string> clientes, string u) => clientes.Count);
            return repo;
        }

        [TestMethod]
        public void OrigenesQueEntran_AmazonYWeb()
        {
            CollectionAssert.AreEqual(new[] { "AMAZON", "WEB" }, ServicioEmbudoClientes.ORIGENES_QUE_ENTRAN.ToArray());
        }

        [TestMethod]
        public async Task Republicar_LosDeOrigenEntranEnElOrdenQueTraeLaConsulta_YSeVeSuOrigen()
        {
            IRepositorioClientesEmbudo repo = Repositorio(
                Candidato("50001"),
                Candidato("50002", "AMAZON", porOrigen: true),
                Candidato("50003", "WEB"),
                Candidato("50004"));

            ResultadoRepublicarEmbudo r = await new ServicioEmbudoClientes(repo).Republicar(new PeticionRepublicarEmbudo { Bloque = "NV-2026-11-16" }, "Carlos");

            CollectionAssert.AreEqual(new[] { "50001", "50002", "50003", "50004" }, r.Clientes);
            Assert.AreEqual(2, r.Origenes.Count);
            Assert.AreEqual("AMAZON", r.Origenes["50002"]);
            Assert.AreEqual("WEB", r.Origenes["50003"], "también el que ya entraba por el vendedor lleva su origen a la vista");
            Assert.AreEqual(1, r.IncluidosPorOrigen);
        }

        [TestMethod]
        public async Task Republicar_DeOrigenSinPersonaConCorreo_NoEntra_AunqueSePidaSinCorreo()
        {
            IRepositorioClientesEmbudo repo = Repositorio(
                Candidato("50002", "AMAZON", porOrigen: true, personasConCorreo: 0),
                Candidato("50003", personasConCorreo: 0));

            ResultadoRepublicarEmbudo r = await new ServicioEmbudoClientes(repo).Republicar(
                new PeticionRepublicarEmbudo { Bloque = "B", SoloConCorreo = false }, "Carlos");

            CollectionAssert.AreEqual(new[] { "50003" }, r.Clientes, "el del vendedor sin correo sí, si se pide sin correo (como antes)");
        }

        [TestMethod]
        public async Task Republicar_DeOrigenYaEnviadoOPendiente_NoEntra()
        {
            IRepositorioClientesEmbudo repo = Repositorio(
                Candidato("50002", "AMAZON", porOrigen: true, yaEnviado: true),
                Candidato("50005", "WEB", porOrigen: true, pendiente: true),
                Candidato("50006", "WEB", porOrigen: true));

            ResultadoRepublicarEmbudo r = await new ServicioEmbudoClientes(repo).Republicar(new PeticionRepublicarEmbudo { Bloque = "B" }, "Carlos");

            CollectionAssert.AreEqual(new[] { "50006" }, r.Clientes);
            Assert.AreEqual(1, r.ExcluidosPorYaEnviados);
            Assert.AreEqual(1, r.ExcluidosPorPendienteSincronizar);
        }

        [TestMethod]
        public async Task Republicar_ElTopeDelBloqueCuentaTambienLosDeOrigen()
        {
            CandidatoEmbudo[] candidatos = Enumerable.Range(1, 60)
                .Select(i => Candidato((60000 + i).ToString(), i % 2 == 0 ? "WEB" : null, porOrigen: i % 2 == 0))
                .ToArray();
            IRepositorioClientesEmbudo repo = Repositorio(candidatos);

            ResultadoRepublicarEmbudo r = await new ServicioEmbudoClientes(repo).Republicar(
                new PeticionRepublicarEmbudo { Bloque = "B", Ensayo = false }, "Carlos");

            Assert.AreEqual(ServicioEmbudoClientes.MAXIMO_POR_BLOQUE, r.Clientes.Count);
            Assert.AreEqual(10, r.CandidatosQueQuedan);
            Assert.AreEqual(25, r.IncluidosPorOrigen);
            A.CallTo(() => repo.Registrar("B", A<IList<string>>.That.Matches(l => l.Count == 50), "Carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void SqlCandidatos_AnadeLosDeOrigenDeCanalSinMirarVendedorNiEstado()
        {
            string sql = RepositorioClientesEmbudoSql.SqlCandidatos(6);

            // Por SQL dinámico: sin las tablas de orígenes (o sin EsLead) la consulta sigue como antes
            StringAssert.Contains(sql, "IF OBJECT_ID(N'dbo.ClientesOrigen', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.OrigenesCliente', N'EsLead') IS NOT NULL");
            StringAssert.Contains(sql, "EXEC sp_executesql");
            StringAssert.Contains(sql, "co.Origen IN (@o0, @o1) AND o.EsLead = 0 AND c.Estado >= 0");
            StringAssert.Contains(sql, "N'@empresa char(3), @o0 varchar(30), @o1 varchar(30)', @empresa, @o0, @o1;");
            StringAssert.Contains(sql, "AND NOT EXISTS (SELECT 1 FROM #AptosEmbudo a WHERE a.Cliente = co.Cliente)");
            // Siempre con alguna persona con correo y boletín, aunque se pida sin correo
            StringAssert.Contains(sql, "WHERE (@soloConCorreo = 0 AND t.EntraPorOrigen = 0) OR t.PersonasConCorreo > 0");
            StringAssert.Contains(sql, "t.Origen, t.EntraPorOrigen");
            // Se añaden DESPUÉS de quitar a los que tienen otro vendedor por grupo de producto (a ellos eso no les afecta)
            Assert.IsTrue(sql.IndexOf("DELETE a FROM #AptosEmbudo", StringComparison.Ordinal) < sql.IndexOf("INSERT INTO #AptosEmbudo", StringComparison.Ordinal));
            // El mismo orden de siempre
            StringAssert.Contains(sql, "ORDER BY CASE WHEN t.UltimoPedido IS NULL THEN 1 ELSE 0 END, t.UltimoPedido DESC, t.FechaAlta DESC");
        }
    }
}
