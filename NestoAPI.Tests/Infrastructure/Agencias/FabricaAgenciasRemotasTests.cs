using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Factory de agencias remotas. NestoAPI#258 (Fase 2): delega en RegistroAgencias (perfiles por
    /// reflexión + puerta de activas por cuarentena), así que la fábrica ya no hardcodea agencias:
    /// una agencia sin perfil, sin fila en AgenciasTransporte o en cuarentena devuelve null.
    /// </summary>
    [TestClass]
    public class FabricaAgenciasRemotasTests
    {
        private static IFabricaAgenciasRemotas CrearFabrica(params AgenciaTransporte[] agencias)
            => CrearFabrica(null, agencias);

        private static IFabricaAgenciasRemotas CrearFabrica(string valorCuarentena, params AgenciaTransporte[] agencias)
        {
            var db = A.Fake<NVEntities>();
            var fakeAgencias = A.Fake<DbSet<AgenciaTransporte>>(o => o.Implements<IQueryable<AgenciaTransporte>>().Implements<IDbAsyncEnumerable<AgenciaTransporte>>());
            A.CallTo(() => db.AgenciasTransportes).Returns(fakeAgencias);
            ConfigurarFakeDbSet(fakeAgencias, agencias.AsQueryable());

            // La puerta de activas lee el parámetro AgenciasEnCuarentena del usuario (defecto).
            ParametroUsuario[] parametros = valorCuarentena == null
                ? new ParametroUsuario[0]
                : new[] { new ParametroUsuario
                    {
                        Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Usuario = GateAgenciasActivasPorCuarentena.USUARIO_GENERAL,
                        Clave = GateAgenciasActivasPorCuarentena.CLAVE_CUARENTENA,
                        Valor = valorCuarentena
                    } };
            var fakeParametros = A.Fake<DbSet<ParametroUsuario>>(o => o.Implements<IQueryable<ParametroUsuario>>().Implements<IDbAsyncEnumerable<ParametroUsuario>>());
            A.CallTo(() => db.ParametrosUsuario).Returns(fakeParametros);
            ConfigurarFakeDbSet(fakeParametros, parametros.AsQueryable());

            return new FabricaAgenciasRemotas(db);
        }

        [TestMethod]
        public void Crear_Innovatrans_DevuelveLaEstrategiaConReintentos()
        {
            IFabricaAgenciasRemotas fabrica = CrearFabrica(
                new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_INNOVATRANS, Nombre = "Innovatrans", Identificador = "91253" });

            IAgenciaRemota agencia = fabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS);

            Assert.IsNotNull(agencia);
            // NestoAPI#288: la factory envuelve la estrategia en el decorador de reintentos de
            // transitorios; los llamantes solo ven IAgenciaRemota.
            Assert.IsInstanceOfType(agencia, typeof(AgenciaRemotaConReintentos));
        }

        [TestMethod]
        public void Crear_AgenciaSinIntegracion_DevuelveNull()
        {
            IFabricaAgenciasRemotas fabrica = CrearFabrica();

            Assert.IsNull(fabrica.Crear(Constantes.Agencias.AGENCIA_GLS));
            Assert.IsNull(fabrica.Crear(Constantes.Agencias.AGENCIA_CANTERAS));
            Assert.IsNull(fabrica.Crear(0));
        }

        [TestMethod]
        public void Crear_InnovatransEnCuarentena_DevuelveNull()
        {
            // NestoAPI#258: la clase de perfil existe, pero la puerta (parámetro AgenciasEnCuarentena)
            // la deja fuera: desactivar una agencia se hace desde la BBDD, sin tocar código.
            IFabricaAgenciasRemotas fabrica = CrearFabrica("Sending, Innovatrans",
                new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_INNOVATRANS, Nombre = "Innovatrans", Identificador = "91253" });

            Assert.IsNull(fabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS));
            Assert.AreEqual(0, fabrica.AgenciasConGestionRemota.Count);
        }

        [TestMethod]
        public void AgenciasConCapacidad_SeDerivanDeLosPerfilesActivos()
        {
            // Sustituye a los antiguos arrays hardcodeados _conGestionRemota/_conSeguimiento.
            IFabricaAgenciasRemotas fabrica = CrearFabrica(
                new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_INNOVATRANS, Nombre = "Innovatrans" },
                new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_GLS, Nombre = "ASM" });

            CollectionAssert.AreEquivalent(new[] { Constantes.Agencias.AGENCIA_INNOVATRANS },
                fabrica.AgenciasConGestionRemota.ToList());
            CollectionAssert.AreEquivalent(new[] { Constantes.Agencias.AGENCIA_INNOVATRANS, Constantes.Agencias.AGENCIA_GLS },
                fabrica.AgenciasConSeguimiento.ToList());
        }

        [TestMethod]
        public void CrearSeguimiento_Gls_DevuelveLaEstrategiaConReintentos()
        {
            IFabricaAgenciasRemotas fabrica = CrearFabrica(
                new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_GLS, Nombre = "ASM" });

            ISeguimientoAgenciaRemota seguimiento = fabrica.CrearSeguimiento(Constantes.Agencias.AGENCIA_GLS);

            Assert.IsNotNull(seguimiento);
            Assert.IsInstanceOfType(seguimiento, typeof(SeguimientoAgenciaRemotaConReintentos));
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
