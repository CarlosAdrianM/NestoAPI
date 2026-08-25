using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Models.Picking
{
    public class GestorPicking
    {
        private ModulosPicking modulos;
        private List<PedidoPicking> candidatos;
        private List<PedidoPicking> retenidosPorPrepago;
        private NVEntities db = new NVEntities();

        public GestorPicking(ModulosPicking modulos)
        {
            this.modulos = modulos;
        }
        /// <summary>
        /// Picking interactivo: el horizonte de entrega se DEDUCE de la hora, como siempre.
        /// </summary>
        public void SacarPicking()
        {
            SacarPicking(CalcularFechaPicking(DateTime.Now));
        }

        /// <summary>
        /// NestoAPI#361: picking con el horizonte de entrega COMO DATO, no deducido del reloj.
        ///
        /// El horizonte decide hasta qué fecha de entrega se sirve
        /// (<c>BorrarLineasEntregaFutura</c> quita las líneas con FechaEntrega mayor), y hasta
        /// ahora salía siempre de <c>CalcularFechaPicking(DateTime.Now)</c>. Eso hacía que el
        /// picking de cierre de las 11h fuera peligrosamente sensible al segundo exacto en que
        /// arrancara: a las 10:59:59 servía HOY, y a las 11:00:01 pasaba a servir también lo de
        /// MAÑANA, adelantando un día las entregas sin que nadie se enterase. Se toreaba
        /// programando la tarea a las 10:59:40, a costa de dejar fuera los pedidos metidos en
        /// esos últimos 20 segundos (que el propio PedidosVentaController sí permite meter,
        /// porque su corte son las 11h en punto).
        ///
        /// El picking de cierre no necesita preguntarle la hora a nadie: ya sabe que sirve para
        /// hoy. Pasándolo como dato, da igual que la tarea arranque a las 11:00:00, a las
        /// 11:00:30 o tarde por lo que sea.
        /// </summary>
        /// <param name="fechaPicking">Fecha de entrega hasta la que se sirve en este picking.</param>
        public void SacarPicking(DateTime fechaPicking)
        {
            EnExclusiva(() =>
            {
                candidatos = modulos.rellenadorPicking.Rellenar();
                Ejecutar(fechaPicking);
            });
        }

        public void SacarPicking(List<Ruta> rutas)
        {
            EnExclusiva(() =>
            {
                candidatos = modulos.rellenadorPicking.Rellenar(rutas);
                Ejecutar(CalcularFechaPicking(DateTime.Now));
            });
        }

        public void SacarPicking(string empresa, int numeroPedido)
        {
            EnExclusiva(() =>
            {
                candidatos = modulos.rellenadorPicking.Rellenar(empresa, numeroPedido);
                Ejecutar(CalcularFechaPicking(DateTime.Now));
            });
        }

        public void SacarPicking(string cliente)
        {
            EnExclusiva(() =>
            {
                candidatos = modulos.rellenadorPicking.Rellenar(cliente);
                Ejecutar(CalcularFechaPicking(DateTime.Now));
            });
        }

        // NestoAPI#405: el picking NO era idempotente frente a dos ejecuciones solapadas.
        //
        // Entre el Rellenar() (que lee las líneas con Picking null) y el SaveChanges del
        // finalizador pasan segundos: reserva de stock, portes, pendientes y ubicaciones. Dos
        // peticiones que entren dentro de esa ventana leen AMBAS las mismas líneas como
        // disponibles y las procesan las dos. El número de picking de la línea se pisa (gana el
        // último UPDATE) y no se nota, pero las ubicaciones NO se pisan: cada pasada reserva la
        // suya, y la línea acaba con el DOBLE de unidades ubicadas. Como el SP del packing suma
        // las ubicaciones de cada línea, la hoja sale con el doble y el almacén serviría de más.
        //
        // Pasó el 25/08/2026 con el picking 99327 (pedidos 924333, 924798 y 924799): la huella
        // fue un número de picking consumido y sin usar, el 99326.
        //
        // Toda la ejecución pasa a ser sección crítica, con el mismo applock de #294. Se libera
        // en el finally, y como los SaveChanges van en autocommit, cuando la segunda entra ya ve
        // las líneas con su picking asignado y el filtro del rellenador las deja fuera.
        private const string RECURSO_BLOQUEO = "Picking:SacarPicking";
        private const int TIMEOUT_BLOQUEO_MS = 120000;  // el picking de cierre es largo

        private void EnExclusiva(Action accion)
        {
            System.Data.Common.DbConnection conexion = db.Database.Connection;
            // El applock de ámbito Session vive mientras viva la CONEXIÓN, así que hay que
            // abrirla a mano: si se deja al pool, EF la devuelve entre operaciones y el bloqueo
            // se soltaría a mitad de picking.
            bool laAbrimosAqui = conexion.State != System.Data.ConnectionState.Open;
            if (laAbrimosAqui)
            {
                conexion.Open();
            }
            try
            {
                _ = db.Database.ExecuteSqlCommand(
                    @"DECLARE @resultado int;
                      EXEC @resultado = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @p1;
                      IF @resultado < 0 RAISERROR('Ya se está sacando otro picking en este momento. Espere a que termine e inténtelo de nuevo.', 16, 1);",
                    RECURSO_BLOQUEO, TIMEOUT_BLOQUEO_MS);
                try
                {
                    accion();
                }
                finally
                {
                    _ = db.Database.ExecuteSqlCommand(
                        "EXEC sp_releaseapplock @Resource = @p0, @LockOwner = 'Session';", RECURSO_BLOQUEO);
                }
            }
            finally
            {
                if (laAbrimosAqui)
                {
                    conexion.Close();
                }
            }
        }


        public List<PedidoPicking> PedidosEnPicking()
        {
            return candidatos;
        }

        private void Ejecutar(DateTime fechaPicking)
        {
            List<StockProducto> stocks;
            List<LineaPedidoPicking> todasLasLineas;

            stocks = modulos.rellenadorStocks.Rellenar(candidatos);

            todasLasLineas = modulos.rellenadorPicking.RellenarTodasLasLineas(candidatos);

            GestorReservasStock.Reservar(stocks, candidatos, todasLasLineas);

            GestorReservasStock.BorrarLineasQueNoDebenSalir(candidatos, fechaPicking);

            // Recorrer Candidatos (quitamos los que no tienen que salir)
            for (int i = 0; i < candidatos.Count(); i++)
            {
                PedidoPicking pedido = candidatos[i];
                GestorStocksPicking gestorStocks = new GestorStocksPicking(pedido);
                if (!pedido.saleEnPicking() || pedido.Lineas.Count == 0 || !gestorStocks.HayStockDeAlgo())
                {
                    pedido.Borrar = true;
                }
                else
                {
                    if (pedido.hayQueSumarPortes())
                    {
                        GeneradorPortes generadorPortes = new GeneradorPortes(db, pedido);
                        generadorPortes.Ejecutar();
                    };
                }
            }

            // Actualizar Pendientes
            GeneradorPendientes generadorPendientes = new GeneradorPendientes(db, candidatos);
            generadorPendientes.Ejecutar();

            retenidosPorPrepago = candidatos.Where(c => c.RetenidoPorPrepago).ToList();
            candidatos.RemoveAll(c => c.Borrar);

            // Asignar Picking
            AsignadorPicking asignadorPicking = new AsignadorPicking(db, candidatos);
            asignadorPicking.Ejecutar();

            // Finalizar Picking
            modulos.finalizador.Ejecutar(db);

            // Si no se ha asignado picking a nada, damos error de NEGOCIO (400, no 500):
            // es un resultado esperable (sin stock o nada que sacar), no un fallo del sistema.
            if (candidatos.Count == 0)
            {
                throw new Infraestructure.Exceptions.NestoBusinessException(
                    "No hay stock suficiente para asignar picking a ninguna línea",
                    new Infraestructure.Exceptions.ErrorContext { ErrorCode = Constantes.Picking.ERROR_SIN_STOCK })
                {
                    IsWarning = true
                };
            }

            // Mandamos el correo con los pedidos que van por debajo del margen
            GestorMargenes gestor = new GestorMargenes();
            gestor.Rellenar(asignadorPicking.numeroPicking);
            gestor.enviarCorreo();
            GestorPrepagos.EnviarCorreo(retenidosPorPrepago);
            // NestoAPI#253: aviso con importe a vendedor y usuario para los pedidos con la casilla
            // marcada. Nunca lanza (un fallo de correo no debe romper el picking).
            GestorAvisosPicking.EnviarCorreos(candidatos,
                vendedor => db.Vendedores.FirstOrDefault(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Número == vendedor)?.Mail?.Trim());
            
        }

        /// <summary>
        /// NestoAPI#361: ¿el instante dado está ya pasado el corte del día? Se extrae para poder
        /// testear el límite exacto, que antes vivía enterrado en una comparación con
        /// DateTime.Now y era intestable sin congelar el reloj. El corte son las 11:00:00 EN
        /// PUNTO: a las 10:59:59 todavía se sirve hoy.
        /// </summary>
        internal static bool CorteDelDiaSuperado(DateTime instante)
        {
            return instante.Hour >= Constantes.Picking.HORA_MAXIMA_AMPLIAR_PEDIDOS;
        }

        /// <summary>
        /// Deduce el horizonte de entrega a partir de la hora. Lo usa el picking INTERACTIVO; el
        /// de cierre recibe el horizonte como dato (ver SacarPicking(DateTime)).
        /// </summary>
        internal static DateTime CalcularFechaPicking(DateTime fechaConHora)
        {
            DateTime fechaSinHora = new DateTime(fechaConHora.Year, fechaConHora.Month, fechaConHora.Day);

            // Si es antes de las 11h devuelve la fecha de hoy (sin hora)
            if (!CorteDelDiaSuperado(fechaConHora))
            {
                return fechaSinHora;
            }

            // Si es después de las 11h devolvemos el siguiente día laboral            
            var fechaDevolver = fechaSinHora.AddDays(1);
            while (GestorFestivos.EsFestivo(fechaDevolver, Constantes.Almacenes.ALGETE))
            {
                fechaDevolver = fechaDevolver.AddDays(1);
            }

            return fechaDevolver;
        }        
    }        
}