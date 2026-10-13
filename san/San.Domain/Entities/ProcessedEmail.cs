namespace San.Domain.Entities;

// One row per email San has dealt with, keyed "<provider>:<message id>".
//
// The mailbox cursor alone (EmailAccount.LastCheckedAt) cannot make reading exactly
// once: Gmail's search is day-granular, so every run re-lists the previous day, and a
// run that reads only part of a busy inbox has to leave the rest for next time. This
// is what lets a run fetch generously and still read each email once.
public class ProcessedEmail
{
    public string Key { get; set; } = "";
    public string Account { get; set; } = "";
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    // filtered | ignore | note | act | failed
    public string Outcome { get; set; } = "";
}
