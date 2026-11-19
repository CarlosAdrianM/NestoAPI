using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#881 (fleco): «repartoCambiado» con Motivo «Parada» cada vez que las paradas del reparto propio cambian por algo que no hace
    /// el repartidor (Ariadna recalcula el fin de ruta). La regla (pura) y el agrupador (uno por operación).
    /// </summary>
    [TestClass]
    public class AvisoParadasRepartoTests
    {
        private const int RP = Constantes.Agencias.AGENCIA_REPARTO_PROPIO;
        private static readonly DateTime HOY = new DateTime(2026, 11, 19);

        private static FotoEnvioParada Foto(short estado, int agencia = RP, DateTime? dia = null, decimal reembolso = 0m, short bultos = 1)
            => new FotoEnvioParada { Agencia = agencia, Estado = estado, FechaEntrega = dia ?? HOY, Reembolso = reembolso, Bultos = bultos };

        private static bool Afecta(FotoEnvioParada antes, FotoEnvioParada despues)
            => CambiosParadasReparto.AfectaAlReparto(EntityState.Modified, antes, despues);

        // ---------------------------------------------------------------- la regla

        [TestMethod]
        public void PasaATramitado_EnLa14_Avisa_EnOtraAgenciaNo()
        {
            Assert.IsTrue(Afecta(Foto(Constantes.Agencias.ESTADO_EN_CURSO), Foto(Constantes.Agencias.ESTADO_TRAMITADO)), "Tramitar / TramitarTodos / ConfirmarTramitacion");
            Assert.IsTrue(Afecta(Foto(Constantes.Agencias.ESTADO_PENDIENTE), Foto(Constantes.Agencias.ESTADO_TRAMITADO)));
            Assert.IsFalse(Afecta(Foto(Constantes.Agencias.ESTADO_EN_CURSO, Constantes.Agencias.AGENCIA_CTT),
                Foto(Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.AGENCIA_CTT)));
            Assert.IsFalse(Afecta(Foto(Constantes.Agencias.ESTADO_PENDIENTE), Foto(Constantes.Agencias.ESTADO_EN_CURSO)), "En curso aún no es parada");
        }

        [TestMethod]
        public void AltaYaTramitada_Avisa_BorradoDeUnaParada_Avisa()
        {
            Assert.IsTrue(CambiosParadasReparto.AfectaAlReparto(EntityState.Added, null, Foto(Constantes.Agencias.ESTADO_TRAMITADO)),
                "Facturar rutas (parada de la nota), nota de cobro, retorno");
            Assert.IsFalse(CambiosParadasReparto.AfectaAlReparto(EntityState.Added, null, Foto(Constantes.Agencias.ESTADO_PENDIENTE)), "Etiqueta pendiente");
            Assert.IsTrue(CambiosParadasReparto.AfectaAlReparto(EntityState.Deleted, Foto(Constantes.Agencias.ESTADO_TRAMITADO), null));
            Assert.IsFalse(CambiosParadasReparto.AfectaAlReparto(EntityState.Deleted, Foto(Constantes.Agencias.ESTADO_ENTREGADO), null));
            Assert.IsFalse(CambiosParadasReparto.AfectaAlReparto(EntityState.Unchanged, Foto(1), Foto(1)));
        }

        [TestMethod]
        public void DejaDeSerParada_Avisa()
        {
            Assert.IsTrue(Afecta(Foto(Constantes.Agencias.ESTADO_TRAMITADO), Foto(Constantes.Agencias.ESTADO_PENDIENTE)), "Anular / devuelto a pendiente");
            Assert.IsTrue(Afecta(Foto(Constantes.Agencias.ESTADO_TRAMITADO), Foto(Constantes.Agencias.ESTADO_CERRADO_A_MANO)));
            Assert.IsTrue(Afecta(Foto(Constantes.Agencias.ESTADO_TRAMITADO), Foto(Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.AGENCIA_CTT)),
                "Cambiado de agencia");
            Assert.IsTrue(Afecta(Foto(Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.AGENCIA_CTT), Foto(Constantes.Agencias.ESTADO_TRAMITADO)),
                "Pasa al reparto propio");
        }

        [TestMethod]
        public void SiendoParada_CambiaDia_Reembolso_OBultos_Avisa_LoDemasNo()
        {
            Assert.IsTrue(Afecta(Foto(1), Foto(1, dia: HOY.AddDays(1))), "Aplazar / día de reparto");
            Assert.IsTrue(Afecta(Foto(1), Foto(1, reembolso: 25m)));
            Assert.IsTrue(Afecta(Foto(1), Foto(1, bultos: 3)));
            Assert.IsTrue(Afecta(Foto(Constantes.Agencias.ESTADO_INCIDENTADO), Foto(Constantes.Agencias.ESTADO_INCIDENTADO, dia: HOY.AddDays(2))));
            Assert.IsFalse(Afecta(Foto(1), Foto(1)), "Observaciones, teléfono…: no cambian la parada");
            Assert.IsFalse(Afecta(Foto(1, dia: HOY.AddHours(9)), Foto(1, dia: HOY.AddHours(15))), "Mismo día");
        }

        [TestMethod]
        public void EntregarODarIncidencia_LoHaceElRepartidor_NoAvisa()
        {
            Assert.IsFalse(Afecta(Foto(1), Foto(Constantes.Agencias.ESTADO_ENTREGADO, dia: HOY.AddHours(11))), "Entregar (ya avisa «Entrega»)");
            Assert.IsFalse(Afecta(Foto(1), Foto(Constantes.Agencias.ESTADO_INCIDENTADO)), "Incidencia (ya avisa «Incidencia»)");
            Assert.IsFalse(Afecta(Foto(Constantes.Agencias.ESTADO_INCIDENTADO), Foto(Constantes.Agencias.ESTADO_ENTREGADO)));
            Assert.IsFalse(Afecta(Foto(Constantes.Agencias.ESTADO_INCIDENTADO), Foto(Constantes.Agencias.ESTADO_TRAMITADO)),
                "De incidentado a tramitado sigue siendo la misma parada");
        }

        // ---------------------------------------------------------------- el agrupador

        private sealed class Reloj
        {
            public DateTime Ahora = HOY.AddHours(9);
            public readonly List<TimeSpan> Esperas = new List<TimeSpan>();
            public TaskCompletionSource<bool> Bloqueo;

            public Task Esperar(TimeSpan t)
            {
                Esperas.Add(t);
                Ahora += t;
                return Bloqueo?.Task ?? Task.CompletedTask;
            }
        }

        [TestMethod]
        public async Task Agrupador_UnaPeticion_AvisaUnaVezTrasElSilencio_ConSuUsuario()
        {
            var reloj = new Reloj();
            var avisos = new List<string>();
            var agrupador = new AgrupadorAvisosParadas(avisos.Add, () => reloj.Ahora, reloj.Esperar);

            agrupador.Pedir(" NUEVAVISION\\Alfredo ");
            await agrupador.Pasada;

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Alfredo" }, avisos);
            Assert.AreEqual(AgrupadorAvisosParadas.SILENCIO, reloj.Esperas[0]);
        }

        [TestMethod]
        public async Task Agrupador_VariasSeguidas_UnSoloAviso_ConElUltimoUsuario()
        {
            var reloj = new Reloj { Bloqueo = new TaskCompletionSource<bool>() };
            var avisos = new List<string>();
            var agrupador = new AgrupadorAvisosParadas(avisos.Add, () => reloj.Ahora, reloj.Esperar);

            agrupador.Pedir("Carlos");
            Task pasada = agrupador.Pasada;
            for (int i = 0; i < 20; i++)
            {
                agrupador.Pedir(i == 19 ? "Alfredo" : null); // TramitarTodos / Facturar rutas: un guardado por envío
            }
            reloj.Bloqueo.SetResult(true);
            await pasada;

            CollectionAssert.AreEqual(new[] { "Alfredo" }, avisos);
            Assert.AreSame(pasada, agrupador.Pasada, "No arranca otra pasada mientras hay una");
        }

        [TestMethod]
        public async Task Agrupador_SinParar_AvisaComoMuchoAlMaximo()
        {
            var reloj = new Reloj();
            var avisos = new List<string>();
            AgrupadorAvisosParadas agrupador = null;
            int peticiones = 0;
            agrupador = new AgrupadorAvisosParadas(avisos.Add, () => reloj.Ahora, t =>
            {
                // Mientras espera, siguen llegando guardados (una facturación de rutas larga)
                Task espera = reloj.Esperar(t);
                if (peticiones++ < 50)
                {
                    agrupador.Pedir("Carlos");
                }
                return espera;
            });

            agrupador.Pedir("Carlos");
            await agrupador.Pasada;

            Assert.AreEqual(1, avisos.Count);
            Assert.IsTrue(reloj.Ahora - HOY.AddHours(9) <= AgrupadorAvisosParadas.MAXIMO + TimeSpan.FromSeconds(1),
                $"Avisa al máximo aunque no paren ({reloj.Ahora - HOY.AddHours(9)})");
        }

        [TestMethod]
        public async Task Agrupador_SiElAvisoFalla_NoLanza_YLaSiguienteVuelveAAvisar()
        {
            var reloj = new Reloj();
            var errores = new List<Exception>();
            int llamadas = 0;
            var agrupador = new AgrupadorAvisosParadas(u => { llamadas++; throw new InvalidOperationException("SignalR caído"); },
                () => reloj.Ahora, reloj.Esperar, errores.Add);

            agrupador.Pedir("Carlos");
            await agrupador.Pasada;
            agrupador.Pedir("Carlos");
            await agrupador.Pasada;

            Assert.AreEqual(2, llamadas);
            Assert.AreEqual(2, errores.Count);
        }
    }
}
