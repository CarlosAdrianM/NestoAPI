using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#607: el comparador de SELECCIÓN (ComparadorAgenciasFactory.ParaSeleccion: GET
    /// Agencias/MasEconomica, etiquetas pendientes y propuesta de envío) respeta el parámetro
    /// AgenciasEnCuarentena («(defecto)», empresa 1), igual que la puerta de perfiles. Una agencia en
    /// cuarentena no se elige nunca, pero su coste se sigue pudiendo calcular (ImporteGasto de un
    /// envío que ya va por ella), como pasa con las sombra.
    /// </summary>
    [TestClass]
    public class CuarentenaComparadorAgenciasTests
    {
        private const string CP_BARCELONA = "08001"; // Peninsular: GLS e Innovatrans cubren
        private const decimal PESO = 3m;

        private static List<AgenciaTransporte> AgenciasReales() => new List<AgenciaTransporte>
        {
            // Nombres con relleno de char, como vienen de la BD.
            new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_GLS, Empresa = "1  ", Nombre = "GLS                 ", RecargoCombustible = 0m },
            new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_INNOVATRANS, Empresa = "1  ", Nombre = "Innovatrans         ", RecargoCombustible = 0m },
            new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_CTT, Empresa = "1  ", Nombre = "CTT                 ", RecargoCombustible = 0m, EsSombra = true }
        };

        private static NVEntities Db(List<AgenciaTransporte> agencias, string valorCuarentena, string usuario = CuarentenaAgencias.USUARIO_GENERAL)
        {
            var db = A.Fake<NVEntities>();
            A.CallTo(() => db.AgenciasTransportes).Returns(FakeSet(agencias));
            var parametros = new List<ParametroUsuario>();
            if (valorCuarentena != null)
            {
                parametros.Add(new ParametroUsuario
                {
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = usuario,
                    Clave = CuarentenaAgencias.CLAVE,
                    Valor = valorCuarentena
                });
            }
            A.CallTo(() => db.ParametrosUsuario).Returns(FakeSet(parametros));
            return db;
        }

        private static OpcionEnvioAgencia MasEconomica(string valorCuarentena, List<AgenciaTransporte> agencias = null)
            => ComparadorAgenciasFactory.ParaSeleccion(Db(agencias ?? AgenciasReales(), valorCuarentena))
                .MasEconomica("1", CP_BARCELONA, PESO, 0m);

        [TestMethod]
        public void ParaSeleccion_AgenciaEnCuarentena_NoSaleEnMasEconomica_YSaleLaOtraAunqueSeaMasCara()
        {
            OpcionEnvioAgencia sinCuarentena = MasEconomica("");
            Assert.IsNotNull(sinCuarentena);
            Assert.AreNotEqual(Constantes.Agencias.AGENCIA_CTT, sinCuarentena.AgenciaId, "La sombra nunca se elige");
            string nombreGanadora = sinCuarentena.AgenciaId == Constantes.Agencias.AGENCIA_GLS ? "GLS" : "Innovatrans";
            int otra = sinCuarentena.AgenciaId == Constantes.Agencias.AGENCIA_GLS
                ? Constantes.Agencias.AGENCIA_INNOVATRANS
                : Constantes.Agencias.AGENCIA_GLS;

            OpcionEnvioAgencia conCuarentena = MasEconomica(nombreGanadora);

            Assert.IsNotNull(conCuarentena, "Queda la otra agencia");
            Assert.AreEqual(otra, conCuarentena.AgenciaId, "La de cuarentena no sale; sale la otra");
            Assert.IsTrue(conCuarentena.Coste >= sinCuarentena.Coste, "La otra no era la más barata");
        }

        [TestMethod]
        public void ParaSeleccion_InnovatransEnCuarentena_NuncaEsLaMasEconomica()
        {
            // El caso del 08/10/26, con mayúsculas y espacios como los teclea la ventana de agencias.
            foreach (string cp in new[] { "28001", "08001", "07001", "1000-001" })
            {
                string pais = cp.Contains("-") ? "PT" : "ES";
                OpcionEnvioAgencia mejor = ComparadorAgenciasFactory.ParaSeleccion(Db(AgenciasReales(), " Sending, correos express ,  INNOVATRANS "))
                    .MasEconomica("1", cp, PESO, 0m, pais);
                Assert.IsTrue(mejor == null || mejor.AgenciaId != Constantes.Agencias.AGENCIA_INNOVATRANS, $"CP {cp}: Innovatrans está en cuarentena");
            }
        }

        [TestMethod]
        public void ParaSeleccion_SombraYCuarentena_SeCombinan()
        {
            // CTT es sombra y GLS está en cuarentena: solo puede salir Innovatrans.
            OpcionEnvioAgencia mejor = MasEconomica("GLS");
            Assert.IsNotNull(mejor);
            Assert.AreEqual(Constantes.Agencias.AGENCIA_INNOVATRANS, mejor.AgenciaId);

            // GLS e Innovatrans en cuarentena y CTT sombra: no hay ninguna seleccionable.
            Assert.IsNull(MasEconomica("GLS, Innovatrans"), "La sombra no pasa a ser seleccionable por quedarse sola");
        }

        [TestMethod]
        public void ParaSeleccion_CuarentenaVaciaOAusente_EsComoHoy()
        {
            var hoy = new ComparadorAgencias(
                new RegistroTarifasExistentes(new RegistroTarifas(), AgenciasReales().Select(a => a.Numero)),
                new FuelCeroCuarentena(),
                new[] { Constantes.Agencias.AGENCIA_CTT });
            OpcionEnvioAgencia esperada = hoy.MasEconomica("1", CP_BARCELONA, PESO, 0m);

            foreach (string valor in new[] { null, "", "   ", "Sending, Correos Express" })
            {
                OpcionEnvioAgencia obtenida = MasEconomica(valor);
                Assert.AreEqual(esperada.AgenciaId, obtenida.AgenciaId, $"Cuarentena «{valor}»");
                Assert.AreEqual(esperada.ServicioId, obtenida.ServicioId, $"Cuarentena «{valor}»");
                Assert.AreEqual(esperada.Coste, obtenida.Coste, $"Cuarentena «{valor}»");
            }
        }

        [TestMethod]
        public void ParaSeleccion_SoloValeLaFilaDefecto_NoLaDeUnUsuario()
        {
            // La cuarentena es general: una fila de un usuario concreto no la activa.
            OpcionEnvioAgencia sinCuarentena = MasEconomica("");
            string nombreGanadora = sinCuarentena.AgenciaId == Constantes.Agencias.AGENCIA_GLS ? "GLS" : "Innovatrans";

            OpcionEnvioAgencia conFilaDeUsuario = ComparadorAgenciasFactory.ParaSeleccion(Db(AgenciasReales(), nombreGanadora, usuario: "NUEVAVISION\\Carlos"))
                .MasEconomica("1", CP_BARCELONA, PESO, 0m);

            Assert.AreEqual(sinCuarentena.AgenciaId, conFilaDeUsuario.AgenciaId);
        }

        [TestMethod]
        public void ParaSeleccion_CosteDeAgenciaEnCuarentena_SeSigueCalculando()
        {
            // Un envío que ya va por la agencia (pendiente creado antes de la cuarentena, o forzado a
            // mano) necesita su ImporteGasto, y la validación de cobertura de la etiqueta no debe
            // decir «no tiene tarifa para la zona» de una agencia que sí la tiene. Igual que las sombra.
            ComparadorAgencias sin = ComparadorAgenciasFactory.ParaSeleccion(Db(AgenciasReales(), ""));
            ComparadorAgencias con = ComparadorAgenciasFactory.ParaSeleccion(Db(AgenciasReales(), "Innovatrans"));

            OpcionEnvioAgencia esperado = sin.CosteDeAgencia("1", CP_BARCELONA, PESO, 0m, Constantes.Agencias.AGENCIA_INNOVATRANS);
            OpcionEnvioAgencia obtenido = con.CosteDeAgencia("1", CP_BARCELONA, PESO, 0m, Constantes.Agencias.AGENCIA_INNOVATRANS);

            Assert.IsNotNull(esperado);
            Assert.IsNotNull(obtenido, "La cuarentena no quita la tarifa: solo impide elegirla");
            Assert.AreEqual(esperado.Coste, obtenido.Coste);
        }

        [TestMethod]
        public void Numeros_ResuelveNombresANumeros_SinRellenoNiMayusculas_YEnTodasLasEmpresas()
        {
            var agencias = new List<AgenciaTransporte>
            {
                new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "GLS       " },
                new AgenciaTransporte { Numero = 12, Empresa = "1  ", Nombre = "Innovatrans    " },
                new AgenciaTransporte { Numero = 32, Empresa = "3  ", Nombre = "Innovatrans    " },
                new AgenciaTransporte { Numero = 13, Empresa = "1  ", Nombre = null }
            };

            CollectionAssert.AreEquivalent(new[] { 12, 32 }, CuarentenaAgencias.Numeros(agencias, " innovatrans , Desconocida").ToList());
            Assert.AreEqual(0, CuarentenaAgencias.Numeros(agencias, null).Count);
            Assert.AreEqual(0, CuarentenaAgencias.Numeros(agencias, " , ").Count);
        }

        private class FuelCeroCuarentena : IProveedorRecargoCombustible
        {
            public decimal RecargoCombustible(string empresa, int agenciaId) => 0m;
        }

        private static DbSet<T> FakeSet<T>(List<T> data) where T : class
        {
            var set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>());
            A.CallTo(() => ((IQueryable<T>)set).Provider).ReturnsLazily(() => data.AsQueryable().Provider);
            A.CallTo(() => ((IQueryable<T>)set).Expression).ReturnsLazily(() => data.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            return set;
        }
    }
}
