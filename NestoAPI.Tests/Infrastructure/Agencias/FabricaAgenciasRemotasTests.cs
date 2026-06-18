using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Models;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Factory de agencias remotas: solo Innovatrans (Numero=12) tiene gestión remota; el resto de
    /// agencias devuelve null (sin integración server-side). El identificador DataTrans se lee de
    /// AgenciaTransporte.Identificador (fila 12), por eso la factory recibe la BD.
    /// </summary>
    [TestClass]
    public class FabricaAgenciasRemotasTests
    {
        private static IFabricaAgenciasRemotas CrearFabrica(params AgenciaTransporte[] agencias)
        {
            var db = A.Fake<NVEntities>();
            var fakeSet = A.Fake<DbSet<AgenciaTransporte>>(o => o.Implements<IQueryable<AgenciaTransporte>>().Implements<IDbAsyncEnumerable<AgenciaTransporte>>());
            A.CallTo(() => db.AgenciasTransportes).Returns(fakeSet);
            ConfigurarFakeDbSet(fakeSet, agencias.AsQueryable());
            return new FabricaAgenciasRemotas(db);
        }

        [TestMethod]
        public void Crear_Innovatrans_DevuelveLaEstrategia()
        {
            IFabricaAgenciasRemotas fabrica = CrearFabrica(
                new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_INNOVATRANS, Identificador = "91253" });

            IAgenciaRemota agencia = fabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS);

            Assert.IsNotNull(agencia);
            Assert.IsInstanceOfType(agencia, typeof(AgenciaRemotaInnovatrans));
        }

        [TestMethod]
        public void Crear_AgenciaSinIntegracion_DevuelveNull()
        {
            IFabricaAgenciasRemotas fabrica = CrearFabrica();

            Assert.IsNull(fabrica.Crear(Constantes.Agencias.AGENCIA_GLS));
            Assert.IsNull(fabrica.Crear(Constantes.Agencias.AGENCIA_CANTERAS));
            Assert.IsNull(fabrica.Crear(0));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider).Returns(data.Provider);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
