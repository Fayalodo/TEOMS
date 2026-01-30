using UnityEngine;

public class TimeMusicController : MonoBehaviour
{
    public AudioSource musicSource;
    public AudioClip morning;
    public AudioClip day;
    public AudioClip evening;
    public AudioClip night;

    void OnEnable()
    {
        WorldTimeSystem.OnTimeOfDayChanged += ChangeMusic;
    }

    void OnDisable()
    {
        WorldTimeSystem.OnTimeOfDayChanged -= ChangeMusic;
    }

    void ChangeMusic(WorldTimeSystem.TimeOfDay time)
    {
        AudioClip clip = time switch
        {
            WorldTimeSystem.TimeOfDay.Morning => morning,
            WorldTimeSystem.TimeOfDay.Day => day,
            WorldTimeSystem.TimeOfDay.Evening => evening,
            WorldTimeSystem.TimeOfDay.Night => night,
            _ => null
        };

        if (clip != null && musicSource.clip != clip)
        {
            musicSource.clip = clip;
            musicSource.Play();
        }
    }
}
