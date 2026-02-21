# 05 - Metrics Event Schema

Genre-agnostic baseline events for all games.

## Common Events

- `first_open`
- `tutorial_step`
- `run_start`
- `run_end`
- `fail_reason`
- `session_end`
- `ad_impression`
- `rewarded_offer`
- `rewarded_accept`
- `purchase_attempt` (if IAP exists)

## Required Parameters

- `game_code`
- `app_version`
- `build_number`
- `session_id`
- `platform` (`android` / `ios`)
- `template_type` (`arcade` / `puzzle` / `defense`)

## Event-Specific Parameters

- `tutorial_step`: `step_index`, `step_name`, `result`
- `run_end`: `duration_sec`, `score`, `outcome`
- `fail_reason`: `reason_code`
- `ad_impression`: `ad_format`, `placement`, `ecpm_bucket`
- `rewarded_accept`: `reward_type`, `reward_amount`
- `purchase_attempt`: `product_id`, `price_tier`, `result`

## First 48h Go/No-Go Dashboard

- tutorial completion rate
- crash-free sessions
- avg session duration
- ad impressions per user
- rewarded accept rate

## A/B Plan Hooks

- primary experiment key: `interstitial_cooldown_seconds` (60/90/120)
- secondary keys by genre:
  - arcade: revive offer timing
  - puzzle: hint cooldown
  - defense: wave reward multiplier

