using Microsoft.ApplicationInsights.WindowsServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class GestorFestivos : IGestorDiasNoLaborables
    {
        public GestorFestivos()
        {
            ListaFestivos = new List<Festivo>();
        }
        public List<Festivo> ListaFestivos { get; set; }
        public void Rellenar(DateTime fechaHasta, DateTime fechaDesde)
        {
            Festivo festivo1 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 1),
                Fiesta = "Fiesta del Trabajo",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo1);
            Festivo festivo2 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 2),
                Fiesta = "Fiesta de la Comunidad de Madrid",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo2);
            Festivo festivo3 = new Festivo()
            {
                Fecha = new DateTime(2017, 8, 15),
                Fiesta = "Asunción de la Virgen",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo3);
            Festivo festivo4 = new Festivo()
            {
                Fecha = new DateTime(2017, 10, 12),
                Fiesta = "Fiesta Nacional",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo4);
            Festivo festivo5 = new Festivo()
            {
                Fecha = new DateTime(2017, 11, 1),
                Fiesta = "Todos los Santos",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo5);
            Festivo festivo6 = new Festivo()
            {
                Fecha = new DateTime(2017, 12, 6),
                Fiesta = "Día de la Constitución",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo6);
            Festivo festivo7 = new Festivo()
            {
                Fecha = new DateTime(2017, 12, 8),
                Fiesta = "Día de la Inmaculada Concepción",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo7);
            Festivo festivo8 = new Festivo()
            {
                Fecha = new DateTime(2017, 12, 25),
                Fiesta = "Natividad del Señor",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo8);
            /*
            // ALGETE
            Festivo festivo9 = new Festivo()
            {
                Fecha = new DateTime(2017, 9, 8),
                Fiesta = "Fiesta local Algete",
                TipoFestivo = TipoFestivo.Algete
            };
            ListaFestivos.Add(festivo9);
            Festivo festivo10 = new Festivo()
            {
                Fecha = new DateTime(2017, 9, 11),
                Fiesta = "Fiesta local Algete",
                TipoFestivo = TipoFestivo.Algete
            };
            ListaFestivos.Add(festivo10);
            */

            //MADRID
            Festivo festivo11 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 15),
                Fiesta = "Fiesta local Madrid (San Isidro)",
                TipoFestivo = TipoFestivo.Madrid
            };
            ListaFestivos.Add(festivo11);
            Festivo festivo12 = new Festivo()
            {
                Fecha = new DateTime(2017, 11, 9),
                Fiesta = "Fiesta local Madrid (Almudena)",
                TipoFestivo = TipoFestivo.Madrid
            };
            ListaFestivos.Add(festivo12);
        }

        public static bool EsFestivo(DateTime fecha, string delegacion)
        {
            var fechaSinHora = new DateTime(fecha.Year, fecha.Month, fecha.Day);

            // Los sábados y los domingos no trabajamos
            if (fechaSinHora.DayOfWeek == DayOfWeek.Saturday || fechaSinHora.DayOfWeek == DayOfWeek.Sunday)
            {
                return true;
            }

            // Buscar en la lista Fiestas si hay algún elemento que coincida con la fecha y el tipo de festivo
            var festivoEncontrado = Fiestas.Any(f => f.Fecha == fechaSinHora && f.TipoFestivo == TipoFestivo.Todas);
            if (festivoEncontrado)
            {
                return true;
            }

            // Verificar fiestas según el valor del parámetro "delegacion"
            switch (delegacion)
            {
                case Constantes.Almacenes.ALGETE:
                    return Fiestas.Any(f => f.Fecha == fechaSinHora && f.TipoFestivo == TipoFestivo.Algete);
                case Constantes.Almacenes.ALCOBENDAS:
                    return Fiestas.Any(f => f.Fecha == fechaSinHora && f.TipoFestivo == TipoFestivo.Alcobendas);
                case Constantes.Almacenes.REINA:
                    return Fiestas.Any(f => f.Fecha == fechaSinHora && f.TipoFestivo == TipoFestivo.Madrid);
                default:
                    return false;
            }
        }

        private static List<Festivo> Fiestas = new List<Festivo>
        {
            new Festivo()
            {
                Fecha = new DateTime(2024, 1, 1),
                Fiesta = "Año nuevo",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 6, 1),
                Fiesta = "Epifanía del Señor",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 3, 28),
                Fiesta = "Jueves Santo",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 3, 29),
                Fiesta = "Viernes Santo",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 5, 1),
                Fiesta = "Día del trabajo",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 5, 2),
                Fiesta = "Día de la Comunidad de Madrid",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 7, 25),
                Fiesta = "Santiago Apostol",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 8, 15),
                Fiesta = "Asunción de la Virgen",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 10, 12),
                Fiesta = "Fiesta Nacional Española",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 1, 11),
                Fiesta = "Día de Todos los Santos",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 12, 6),
                Fiesta = "Día de la Constitución Española",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 12, 25),
                Fiesta = "Navidad",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 5, 15),
                Fiesta = "San Isidro Labrador",
                TipoFestivo = TipoFestivo.Madrid
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 11, 9),
                Fiesta = "Nuestra Señora de la Almudena",
                TipoFestivo = TipoFestivo.Madrid
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 1, 24),
                Fiesta = "Nuestra Señora de la Paz",
                TipoFestivo = TipoFestivo.Alcobendas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 5, 15),
                Fiesta = "San Isidro",
                TipoFestivo = TipoFestivo.Alcobendas
            },
            /*
            new Festivo()
            {
                Fecha = new DateTime(2024, 9, 13),
                Fiesta = "Fiestas de Algete (viernes)",
                TipoFestivo = TipoFestivo.Algete
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 9, 16),
                Fiesta = "Fiestas de Algete (lunes)",
                TipoFestivo = TipoFestivo.Algete
            },
            */
            new Festivo()
            {
                Fecha = new DateTime(2024, 12, 24),
                Fiesta = "Nochebuena",
                TipoFestivo = TipoFestivo.Todas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 12, 31),
                Fiesta = "Nochevieja",
                TipoFestivo = TipoFestivo.Todas
            }
        };
    }
}